using DvCms.Contracts.Accessi;
using DvCms.Contracts.Builder;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Core.Builder;
using DvCms.Core.Cms;
using DvCms.Data;
using DvCms.DTO.Builder;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Accessi;
using DvCms.Models.Cms;
using DvCms.Models.Media;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Services.Cms;

public sealed class ServizioBuilder : IServizioBuilder
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly RegistroModelliPagina _modelli;
    private readonly RegistroTemplateBuilder _template;
    private readonly RegistroComponenti _componenti;
    private readonly TimeProvider _orologio;

    public ServizioBuilder(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IServizioAudit audit,
        RegistroModelliPagina modelli,
        RegistroTemplateBuilder template,
        RegistroComponenti componenti,
        TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _modelli = modelli;
        _template = template;
        _componenti = componenti;
        _orologio = orologio;
    }

    public async Task<StatoBuilder?> LeggiAsync(Guid idAttore, Guid idPagina, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        var idSito = _tenant.IdSito;
        var pagina = await _contesto.Pagine.AsNoTracking().SingleOrDefaultAsync(candidata => candidata.Id == idPagina && candidata.IdSito == idSito, annullamento);
        if (pagina?.IdUltimaRevisioneBozza is not { } idBozza)
        {
            return null;
        }

        var bozza = await _contesto.RevisioniPagina.AsNoTracking().SingleAsync(revisione => revisione.Id == idBozza, annullamento);
        if (TrovaTemplate(bozza) is not { } template || bozza.Builder is not { } documento)
        {
            return null;
        }

        var codiceLingua = await _contesto.Lingue.Where(lingua => lingua.Id == pagina.IdLingua).Select(lingua => lingua.Codice).SingleAsync(annullamento);
        return new StatoBuilder(
            pagina.Id,
            bozza.Titolo,
            codiceLingua,
            template.Id,
            template.Versione,
            bozza.NumeroRevisione,
            documento.ProgettoEditorJson,
            Convert.ToBase64String(pagina.TokenConcorrenza),
            CatalogoEditor.Catalogo(_componenti),
            CatalogoEditor.Vincoli(template));
    }

    public async Task<EsitoSalvataggioBuilder> SalvaAsync(
        Guid idAttore,
        Guid idPagina,
        string? tokenConcorrenza,
        string? progettoEditorJson,
        bool automatico,
        CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore);
            return Esito(CodiceEsito.NonAutorizzato);
        }

        if (!TentaToken(tokenConcorrenza, out var token))
        {
            return Esito(CodiceEsito.DatiNonValidi, new ErroreBuilder("TokenNonValido"));
        }

        var idSito = _tenant.IdSito;
        var pagina = await _contesto.Pagine.SingleOrDefaultAsync(candidata => candidata.Id == idPagina && candidata.IdSito == idSito, annullamento);
        if (pagina?.IdUltimaRevisioneBozza is not { } idBozza)
        {
            return Esito(CodiceEsito.NonTrovato);
        }

        var bozza = await _contesto.RevisioniPagina.SingleAsync(revisione => revisione.Id == idBozza, annullamento);
        if (TrovaTemplate(bozza) is not { } template)
        {
            // La pagina non usa il builder: non c'è nulla da salvare qui.
            return Esito(CodiceEsito.NonConsentito);
        }

        if (!AdapterGrapesJs.TentaDaProgetto(progettoEditorJson, _componenti, out var convertito, out var erroriConversione))
        {
            return Esito(CodiceEsito.DatiNonValidi, [.. erroriConversione]);
        }

        var documento = _componenti.Normalizza(convertito);
        var errori = new List<ErroreBuilder>();
        errori.AddRange(_componenti.Valida(documento));
        errori.AddRange(ValidatoreTemplate.Valida(template, documento));
        if (errori.Count == 0)
        {
            errori.AddRange(await VerificaRisorseAsync(documento, annullamento));
            errori.AddRange(await VerificaProdottiAsync(documento, annullamento));
            errori.AddRange(await VerificaCategorieAsync(documento, annullamento));
        }

        if (errori.Count > 0)
        {
            return Esito(CodiceEsito.DatiNonValidi, [.. errori.Take(RegistroComponenti.ErroriMassimi)]);
        }

        var json = SerializzatoreDocumento.Scrivi(documento);
        var progetto = AdapterGrapesJs.AProgetto(documento);

        // Nessuna modifica effettiva: nessuna nuova revisione (l'autosave non deve riempire la cronologia).
        if (string.Equals(bozza.DocumentoRenderJson, json, StringComparison.Ordinal) && token.AsSpan().SequenceEqual(pagina.TokenConcorrenza))
        {
            return new EsitoSalvataggioBuilder(CodiceEsito.Riuscito, [], Convert.ToBase64String(pagina.TokenConcorrenza), progetto, bozza.NumeroRevisione);
        }

        try
        {
            _contesto.Entry(pagina).Property(candidata => candidata.TokenConcorrenza).OriginalValue = token;

            // La revisione automatica in cima, non pubblicata e dello stesso autore, è superata da questa: si toglie (mai una
            // revisione manuale né una pubblicata). I numeri di revisione non si riusano: la cronologia può avere dei salti.
            var daSostituire = bozza.SalvataggioAutomatico && bozza.CreatoDaIdUtenza == attore.Id && pagina.IdRevisionePubblicata != bozza.Id ? bozza : null;
            var nuova = pagina.AggiungiRevisione(
                new ContenutoRevisione(
                    bozza.Titolo,
                    bozza.Contenuto,
                    bozza.IdModello,
                    bozza.VersioneModello,
                    bozza.DatiModello,
                    bozza.TitoloSeo,
                    bozza.DescrizioneSeo,
                    bozza.NonIndicizzare,
                    new DocumentoBuilder(progetto, json)),
                automatico ? "Salvataggio automatico" : "Modifica con il builder",
                attore.Id,
                _orologio,
                automatico);
            _contesto.RevisioniPagina.Add(nuova);
            if (daSostituire is not null)
            {
                _contesto.RevisioniPagina.Remove(daSostituire);
            }

            await _contesto.SaveChangesAsync(annullamento);
            return new EsitoSalvataggioBuilder(CodiceEsito.Riuscito, [], Convert.ToBase64String(pagina.TokenConcorrenza), progetto, nuova.NumeroRevisione, NuovaRevisione: true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Esito(CodiceEsito.Conflitto);
        }
    }

    private static EsitoSalvataggioBuilder Esito(CodiceEsito codice, params ErroreBuilder[] errori) => new(codice, errori);

    private TemplateBuilder? TrovaTemplate(RevisionePagina revisione) =>
        _modelli.Trova(revisione.IdModello, revisione.VersioneModello) is { Builder: true } modello
            ? _template.Trova(modello.Id, modello.Versione)
            : null;

    /// <summary>Le immagini devono essere risorse pubbliche del sito dell'host (il filtro di tenant le limita già al tenant).</summary>
    private async Task<List<ErroreBuilder>> VerificaRisorseAsync(DocumentoCms documento, CancellationToken annullamento)
    {
        var richieste = documento.Visita()
            .Where(nodo => nodo.Tipo == "contenuto.immagine" && nodo.Proprieta.ContainsKey("idRisorsa"))
            .Select(nodo => (nodo.Id, Risorsa: Guid.Parse(nodo.Proprieta["idRisorsa"])))
            .ToList();
        if (richieste.Count == 0)
        {
            return [];
        }

        var idSito = _tenant.IdSito;
        var ids = richieste.Select(r => r.Risorsa).Distinct().ToList();
        var valide = (await _contesto.RisorseMedia.AsNoTracking()
            .Where(risorsa => ids.Contains(risorsa.Id) && risorsa.IdSito == idSito
                && risorsa.Visibilita == VisibilitaMedia.Pubblica && risorsa.StatoScansione != StatoScansioneMedia.InQuarantena)
            .Select(risorsa => risorsa.Id)
            .ToListAsync(annullamento)).ToHashSet();
        return [.. richieste.Where(r => !valide.Contains(r.Risorsa)).Select(r => new ErroreBuilder("RisorsaNonDisponibile", r.Id, "idRisorsa"))];
    }

    /// <summary>I prodotti scelti devono esistere nel sito dell'host e non essere archiviati (bozze sì: si prepara la pagina prima di pubblicare). Il renderer mostra solo i pubblicati.</summary>
    private async Task<List<ErroreBuilder>> VerificaProdottiAsync(DocumentoCms documento, CancellationToken annullamento)
    {
        var richieste = documento.Visita()
            .Where(nodo => nodo.Tipo == "negozio.scheda-prodotto" && nodo.Proprieta.ContainsKey("idProdotto"))
            .Select(nodo => (nodo.Id, Prodotto: Guid.Parse(nodo.Proprieta["idProdotto"])))
            .ToList();
        if (richieste.Count == 0)
        {
            return [];
        }

        var idSito = _tenant.IdSito;
        var ids = richieste.Select(r => r.Prodotto).Distinct().ToList();
        var validi = (await _contesto.Prodotti.AsNoTracking()
            .Where(prodotto => ids.Contains(prodotto.Id) && prodotto.IdSito == idSito && prodotto.Stato != DvCms.Models.Negozio.StatoProdotto.Archiviato)
            .Select(prodotto => prodotto.Id)
            .ToListAsync(annullamento)).ToHashSet();
        return [.. richieste.Where(r => !validi.Contains(r.Prodotto)).Select(r => new ErroreBuilder("ProdottoNonDisponibile", r.Id, "idProdotto"))];
    }

    /// <summary>Le categorie scelte devono esistere nel sito dell'host.</summary>
    private async Task<List<ErroreBuilder>> VerificaCategorieAsync(DocumentoCms documento, CancellationToken annullamento)
    {
        var richieste = documento.Visita()
            .Where(nodo => nodo.Tipo == "negozio.griglia-categoria" && nodo.Proprieta.ContainsKey("idCategoria"))
            .Select(nodo => (nodo.Id, Categoria: Guid.Parse(nodo.Proprieta["idCategoria"])))
            .ToList();
        if (richieste.Count == 0)
        {
            return [];
        }

        var idSito = _tenant.IdSito;
        var ids = richieste.Select(r => r.Categoria).Distinct().ToList();
        var valide = (await _contesto.Categorie.AsNoTracking()
            .Where(categoria => ids.Contains(categoria.Id) && categoria.IdSito == idSito)
            .Select(categoria => categoria.Id)
            .ToListAsync(annullamento)).ToHashSet();
        return [.. richieste.Where(r => !valide.Contains(r.Categoria)).Select(r => new ErroreBuilder("CategoriaNonDisponibile", r.Id, "idCategoria"))];
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

    private async Task RegistraNegazioneAsync(Guid idAttore)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore);
        await _audit.RegistraAsync(
            AzioniAudit.OperazioneNegata,
            "Pagina",
            RegistroAudit.EsitoNegato,
            _tenant.IdTenant,
            attoreEsiste ? idAttore : null,
            "azione richiesta: ModificaBuilder",
            CancellationToken.None);
    }

    private static bool TentaToken(string? testo, out byte[] token)
    {
        token = [];
        if (string.IsNullOrWhiteSpace(testo))
        {
            return false;
        }

        try
        {
            token = Convert.FromBase64String(testo);
            return token.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
