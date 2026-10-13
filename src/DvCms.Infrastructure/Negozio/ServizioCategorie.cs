using DvCms.Contracts.Accessi;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Extender;
using DvCms.Infrastructure.Cms;
using DvCms.Infrastructure.Persistenza;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>Categorie del negozio (vedi <see cref="IServizioCategorie"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed class ServizioCategorie : IServizioCategorie
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioCategorie(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<IReadOnlyList<CategoriaVista>> ElencaAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        if (!await CommercioAttivoAsync(annullamento))
        {
            return [];
        }

        var idSito = _tenant.IdSito;
        var lingue = await _contesto.Lingue.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Codice, annullamento);
        var predefinita = (await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento)).FirstOrDefault(l => l.Predefinita)?.IdLingua;
        var categorie = await _contesto.Categorie.AsNoTracking().Where(c => c.IdSito == idSito).OrderBy(c => c.Posizione).ToListAsync(annullamento);
        var nomi = await _contesto.TraduzioniCategoria.AsNoTracking().Where(t => t.IdSito == idSito).ToListAsync(annullamento);
        var conteggi = await _contesto.ProdottiCategoria.AsNoTracking().Where(p => p.IdSito == idSito).GroupBy(p => p.IdCategoria).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, annullamento);

        return [.. categorie
            .Select(c => new CategoriaVista(
                c.Id,
                c.Posizione,
                [.. nomi.Where(n => n.IdCategoria == c.Id).Select(n => new NomeCategoriaDati(lingue.GetValueOrDefault(n.IdLingua) ?? string.Empty, n.Nome))],
                conteggi.GetValueOrDefault(c.Id)))
            .OrderBy(c => c.Posizione)
            .ThenBy(c => c.Nomi.FirstOrDefault(n => predefinita is null || n.CodiceLingua.Length > 0)?.Nome, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<EsitoOperazione> SalvaAsync(Guid idAttore, DatiCategoria dati, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "SalvataggioCategoria");
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!await CommercioAttivoAsync(annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var idTenant = _tenant.IdTenant;
        var idSito = _tenant.IdSito;
        var lingueSito = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
        var predefinita = lingueSito.FirstOrDefault(l => l.Predefinita);
        var perCodice = lingueSito.ToDictionary(l => l.Codice, StringComparer.OrdinalIgnoreCase);

        var errori = Valida(dati, predefinita?.Codice, perCodice);
        if (errori.Count > 0)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, errori);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        Categoria? categoria = null;
        if (dati.Id is { } id)
        {
            categoria = await _contesto.Categorie.SingleOrDefaultAsync(c => c.Id == id && c.IdSito == idSito, annullamento);
            if (categoria is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }
        }
        else if (await _contesto.Categorie.CountAsync(c => c.IdSito == idSito, annullamento) >= Categoria.NumeroMassimo)
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var nuova = categoria is null;
        categoria ??= new Categoria { Id = Identificativi.Nuovo(_orologio), IdTenant = idTenant, IdSito = idSito, CreatoIlUtc = adesso };
        if (nuova)
        {
            _contesto.Categorie.Add(categoria);
        }

        categoria.Posizione = dati.Posizione;
        categoria.AggiornatoIlUtc = adesso;

        var esistenti = nuova ? [] : await _contesto.TraduzioniCategoria.Where(t => t.IdCategoria == categoria.Id).ToListAsync(annullamento);
        var volute = dati.Nomi.Where(n => !string.IsNullOrWhiteSpace(n.Nome)).ToDictionary(n => perCodice[n.CodiceLingua.Trim()].IdLingua, n => n.Nome.Trim());
        _contesto.TraduzioniCategoria.RemoveRange(esistenti.Where(t => !volute.ContainsKey(t.IdLingua)));
        foreach (var (idLingua, nome) in volute)
        {
            var attuale = esistenti.SingleOrDefault(t => t.IdLingua == idLingua);
            if (attuale is null)
            {
                _contesto.TraduzioniCategoria.Add(new TraduzioneCategoria { IdTenant = idTenant, IdSito = idSito, IdCategoria = categoria.Id, IdLingua = idLingua, Nome = nome });
            }
            else
            {
                attuale.Nome = nome;
            }
        }

        await _contesto.SaveChangesAsync(annullamento);
        await _audit.RegistraAsync(
            nuova ? AzioniAudit.CreazioneCategoria : AzioniAudit.ModificaCategoria,
            $"Categoria:{categoria.Id}",
            RegistroAudit.EsitoRiuscito,
            idTenant,
            attore.Id,
            $"{volute.Count} nomi",
            annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, categoria.Id);
    }

    public async Task<EsitoOperazione> EliminaAsync(Guid idAttore, Guid idCategoria, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "EliminazioneCategoria");
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!await CommercioAttivoAsync(annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var idSito = _tenant.IdSito;
        var categoria = await _contesto.Categorie.SingleOrDefaultAsync(c => c.Id == idCategoria && c.IdSito == idSito, annullamento);
        if (categoria is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        _contesto.ProdottiCategoria.RemoveRange(await _contesto.ProdottiCategoria.Where(p => p.IdCategoria == idCategoria).ToListAsync(annullamento));
        _contesto.TraduzioniCategoria.RemoveRange(await _contesto.TraduzioniCategoria.Where(t => t.IdCategoria == idCategoria).ToListAsync(annullamento));
        _contesto.Categorie.Remove(categoria);
        await _contesto.SaveChangesAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.EliminazioneCategoria, $"Categoria:{idCategoria}", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, attore.Id, null, annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idCategoria);
    }

    private static List<string> Valida(DatiCategoria dati, string? codicePredefinito, Dictionary<string, LinguaAbilitata> perCodice)
    {
        var errori = new List<string>();
        void Errore(string campo)
        {
            if (!errori.Contains(campo))
            {
                errori.Add(campo);
            }
        }

        if (dati.Posizione is < 0 or > 9999)
        {
            Errore("Posizione");
        }

        var viste = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nome in dati.Nomi ?? [])
        {
            var codice = nome.CodiceLingua?.Trim() ?? string.Empty;
            if (!perCodice.ContainsKey(codice) || !viste.Add(codice))
            {
                Errore("Nomi");
                continue;
            }

            var testo = nome.Nome?.Trim() ?? string.Empty;
            if (testo.Length > Categoria.LunghezzaMassimaNome || testo.Any(char.IsControl))
            {
                Errore("Nome");
            }
        }

        var predefinitoPresente = codicePredefinito is not null
            && (dati.Nomi ?? []).Any(n => string.Equals(n.CodiceLingua?.Trim(), codicePredefinito, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(n.Nome));
        if (!predefinitoPresente)
        {
            Errore("Nome");
        }

        return errori;
    }

    private Task<bool> CommercioAttivoAsync(CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        return _contesto.Siti.AsNoTracking().AnyAsync(sito => sito.Id == idSito && sito.CommercioAttivo, annullamento);
    }

    private Task<Utenza?> TrovaAttoreAsync(Guid idAttore, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin
                    || (candidata.Ruolo == RuoloUtenza.TenantAdmin && candidata.IdTenant == idTenant)),
            annullamento);
    }

    private async Task RegistraNegazioneAsync(Guid idAttore, string azione)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore);
        await _audit.RegistraAsync(AzioniAudit.OperazioneNegata, "Categoria", RegistroAudit.EsitoNegato, _tenant.IdTenant, attoreEsiste ? idAttore : null, $"azione richiesta: {azione}", CancellationToken.None);
    }
}
