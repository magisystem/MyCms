using DvCms.Application.Accessi;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain;
using DvCms.Domain.Accessi;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>Pagamenti manuali del sito (vedi <see cref="IServizioPagamentiOffline"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed class ServizioPagamentiOffline : IServizioPagamentiOffline
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioPagamentiOffline(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<SchedaPagamentoOffline> LeggiAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        var idSito = _tenant.IdSito;
        var riga = await _contesto.ImpostazioniPagamentoOfflineSiti.AsNoTracking().SingleOrDefaultAsync(i => i.IdSito == idSito, annullamento);
        return riga is null
            ? new SchedaPagamentoOffline(ImpostazioniPagamentoOffline.Spente, null)
            : new SchedaPagamentoOffline(Vista(riga), Convert.ToBase64String(riga.TokenConcorrenza));
    }

    public async Task<EsitoOperazione> SalvaAsync(Guid idAttore, ImpostazioniPagamentoOffline dati, string? tokenConcorrenza, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idSito = _tenant.IdSito;
        if (!await _contesto.Siti.AsNoTracking().AnyAsync(s => s.Id == idSito && s.CommercioAttivo, annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var errori = Valida(dati);
        if (errori.Count > 0)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, errori);
        }

        var riga = await _contesto.ImpostazioniPagamentoOfflineSiti.SingleOrDefaultAsync(i => i.IdSito == idSito, annullamento);
        if (riga is not null && !string.IsNullOrWhiteSpace(tokenConcorrenza))
        {
            try
            {
                _contesto.Entry(riga).Property(i => i.TokenConcorrenza).OriginalValue = Convert.FromBase64String(tokenConcorrenza);
            }
            catch (FormatException)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["TokenConcorrenza"]);
            }
        }

        if (riga is null)
        {
            riga = new ImpostazioniPagamentoOfflineSito { Id = Identificativi.Nuovo(_orologio), IdTenant = _tenant.IdTenant, IdSito = idSito };
            _contesto.ImpostazioniPagamentoOfflineSiti.Add(riga);
        }

        riga.BonificoAttivo = dati.BonificoAttivo;
        riga.Intestatario = Pulito(dati.Intestatario);
        riga.Iban = Iban.Normalizza(dati.Iban);
        riga.Bic = CodiceBic.Normalizza(dati.Bic);
        riga.NomeBanca = Pulito(dati.NomeBanca);
        riga.GiorniPerPagare = dati.GiorniPerPagare;
        riga.IstruzioniBonifico = Pulito(dati.IstruzioniBonifico);
        riga.ContrassegnoAttivo = dati.ContrassegnoAttivo;
        riga.CommissioneFissa = dati.CommissioneFissa;
        riga.CommissionePercentuale = dati.CommissionePercentuale;
        riga.CommissioneGratisSopra = dati.CommissioneGratisSopra;
        riga.OrdineMinimo = dati.OrdineMinimo;
        riga.OrdineMassimo = dati.OrdineMassimo;
        riga.IstruzioniContrassegno = Pulito(dati.IstruzioniContrassegno);
        riga.AggiornatoIlUtc = _orologio.GetUtcNow().UtcDateTime;
        riga.AggiornatoDaIdUtenza = attore.Id;

        try
        {
            await _audit.RegistraAsync(
                AzioniAudit.ModificaPagamentiOffline,
                $"Sito:{idSito}",
                RegistroAudit.EsitoRiuscito,
                _tenant.IdTenant,
                attore.Id,
                $"bonifico {(riga.BonificoAttivo ? "attivo" : "spento")}, contrassegno {(riga.ContrassegnoAttivo ? "attivo" : "spento")}",
                annullamento);
        }
        catch (DbUpdateException)
        {
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        return new EsitoOperazione(CodiceEsito.Riuscito, riga.Id);
    }

    public async Task<ImpostazioniPagamentoOffline> LeggiPerClienteAsync(Guid idAccount, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        var autorizzato = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            candidata => candidata.Id == idAccount
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin
                    || (candidata.IdTenant == idTenant && (candidata.Ruolo == RuoloUtenza.Utente || candidata.Ruolo == RuoloUtenza.TenantAdmin))),
            annullamento);
        if (!autorizzato)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli account autenticati del tenant.");
        }

        var idSito = _tenant.IdSito;
        if (!await _contesto.Siti.AsNoTracking().AnyAsync(s => s.Id == idSito && s.CommercioAttivo, annullamento))
        {
            return ImpostazioniPagamentoOffline.Spente;
        }

        var riga = await _contesto.ImpostazioniPagamentoOfflineSiti.AsNoTracking().SingleOrDefaultAsync(i => i.IdSito == idSito, annullamento);
        if (riga is null)
        {
            return ImpostazioniPagamentoOffline.Spente;
        }

        // Un metodo spento non mostra nulla di sé.
        var completa = Vista(riga);
        var spente = ImpostazioniPagamentoOffline.Spente;
        return completa with
        {
            Intestatario = completa.BonificoAttivo ? completa.Intestatario : spente.Intestatario,
            Iban = completa.BonificoAttivo ? completa.Iban : spente.Iban,
            Bic = completa.BonificoAttivo ? completa.Bic : spente.Bic,
            NomeBanca = completa.BonificoAttivo ? completa.NomeBanca : spente.NomeBanca,
            IstruzioniBonifico = completa.BonificoAttivo ? completa.IstruzioniBonifico : spente.IstruzioniBonifico,
            CommissioneFissa = completa.ContrassegnoAttivo ? completa.CommissioneFissa : spente.CommissioneFissa,
            CommissionePercentuale = completa.ContrassegnoAttivo ? completa.CommissionePercentuale : spente.CommissionePercentuale,
            CommissioneGratisSopra = completa.ContrassegnoAttivo ? completa.CommissioneGratisSopra : spente.CommissioneGratisSopra,
            OrdineMinimo = completa.ContrassegnoAttivo ? completa.OrdineMinimo : spente.OrdineMinimo,
            OrdineMassimo = completa.ContrassegnoAttivo ? completa.OrdineMassimo : spente.OrdineMassimo,
            IstruzioniContrassegno = completa.ContrassegnoAttivo ? completa.IstruzioniContrassegno : spente.IstruzioniContrassegno,
        };
    }

    /// <summary>Le impostazioni correnti del sito dell'host per chi lavora già dentro un'operazione (creazione ordine, notifiche): nessun controllo d'attore.</summary>
    internal static ImpostazioniPagamentoOffline Vista(ImpostazioniPagamentoOfflineSito r) => new(
        r.BonificoAttivo,
        r.Intestatario,
        r.Iban,
        r.Bic,
        r.NomeBanca,
        r.GiorniPerPagare,
        r.IstruzioniBonifico,
        r.ContrassegnoAttivo,
        r.CommissioneFissa,
        r.CommissionePercentuale,
        r.CommissioneGratisSopra,
        r.OrdineMinimo,
        r.OrdineMassimo,
        r.IstruzioniContrassegno);

    private static List<string> Valida(ImpostazioniPagamentoOffline d)
    {
        var errori = new List<string>();

        // Il bonifico attivo richiede intestatario e IBAN; i valori presenti si controllano comunque, anche a metodo spento.
        if (d.BonificoAttivo && string.IsNullOrWhiteSpace(d.Intestatario))
        {
            errori.Add("Intestatario");
        }

        if (!TestoValido(d.Intestatario, ImpostazioniPagamentoOfflineSito.LunghezzaMassimaIntestatario, consentiAcapo: false))
        {
            AggiungiSeManca(errori, "Intestatario");
        }

        if (d.BonificoAttivo && string.IsNullOrWhiteSpace(d.Iban))
        {
            errori.Add("Iban");
        }
        else if (!string.IsNullOrWhiteSpace(d.Iban) && !Iban.Valido(d.Iban))
        {
            errori.Add("Iban");
        }

        if (!string.IsNullOrWhiteSpace(d.Bic) && !CodiceBic.Valido(d.Bic))
        {
            errori.Add("Bic");
        }

        if (!TestoValido(d.NomeBanca, ImpostazioniPagamentoOfflineSito.LunghezzaMassimaBanca, consentiAcapo: false))
        {
            errori.Add("NomeBanca");
        }

        if (d.GiorniPerPagare is < ImpostazioniPagamentoOfflineSito.GiorniPerPagareMinimi or > ImpostazioniPagamentoOfflineSito.GiorniPerPagareMassimi)
        {
            errori.Add("GiorniPerPagare");
        }

        if (!TestoValido(d.IstruzioniBonifico, ImpostazioniPagamentoOfflineSito.LunghezzaMassimaIstruzioni, consentiAcapo: true))
        {
            errori.Add("IstruzioniBonifico");
        }

        if (!ImpostazioniPagamentoOfflineSito.ImportoValido(d.CommissioneFissa))
        {
            errori.Add("CommissioneFissa");
        }

        if (!ImpostazioniPagamentoOfflineSito.PercentualeValida(d.CommissionePercentuale))
        {
            errori.Add("CommissionePercentuale");
        }

        foreach (var (nome, valore) in new[] { ("CommissioneGratisSopra", d.CommissioneGratisSopra), ("OrdineMinimo", d.OrdineMinimo), ("OrdineMassimo", d.OrdineMassimo) })
        {
            if (valore is { } importo && !ImpostazioniPagamentoOfflineSito.ImportoValido(importo))
            {
                errori.Add(nome);
            }
        }

        if (d.OrdineMinimo is { } minimo && d.OrdineMassimo is { } massimo && minimo > massimo && !errori.Contains("OrdineMinimo") && !errori.Contains("OrdineMassimo"))
        {
            errori.Add("OrdineMassimo");
        }

        if (!TestoValido(d.IstruzioniContrassegno, ImpostazioniPagamentoOfflineSito.LunghezzaMassimaIstruzioni, consentiAcapo: true))
        {
            errori.Add("IstruzioniContrassegno");
        }

        return errori;
    }

    private static void AggiungiSeManca(List<string> errori, string campo)
    {
        if (!errori.Contains(campo))
        {
            errori.Add(campo);
        }
    }

    private static bool TestoValido(string? testo, int lunghezzaMassima, bool consentiAcapo) =>
        testo is null
        || (testo.Trim().Length <= lunghezzaMassima && !testo.Any(c => char.IsControl(c) && !(consentiAcapo && c is '\n' or '\r')));

    private static string? Pulito(string? testo) => string.IsNullOrWhiteSpace(testo) ? null : testo.Trim();

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

    private async Task RegistraNegazioneAsync(Guid idAttore)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore);
        await _audit.RegistraAsync(
            AzioniAudit.OperazioneNegata,
            "PagamentiOffline",
            RegistroAudit.EsitoNegato,
            _tenant.IdTenant,
            attoreEsiste ? idAttore : null,
            "azione richiesta: ModificaPagamentiOffline",
            CancellationToken.None);
    }
}
