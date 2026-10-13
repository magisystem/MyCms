using System.Text.RegularExpressions;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Core.Negozio;
using DvCms.Data;
using DvCms.Data.Cms;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Extender;
using DvCms.Infrastructure.Cms;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>Ordini (vedi <see cref="IServizioOrdini"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed partial class ServizioOrdini : IServizioOrdini
{
    private const int LimiteElenco = 200;

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioCarrello _carrello;
    private readonly IServizioGiacenze _giacenze;
    private readonly IServizioAudit _audit;
    private readonly NotificheOrdine _notifiche;
    private readonly OpzioniNegozio _opzioni;
    private readonly TimeProvider _orologio;

    public ServizioOrdini(ContestoDatabase contesto, IContestoTenant tenant, IServizioCarrello carrello, IServizioGiacenze giacenze, IServizioAudit audit, NotificheOrdine notifiche, OpzioniNegozio opzioni, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _carrello = carrello;
        _giacenze = giacenze;
        _audit = audit;
        _notifiche = notifiche;
        _opzioni = opzioni;
        _orologio = orologio;
    }

    public Task<EsitoOperazione> CreaDaCarrelloAsync(Guid idAccount, string codiceLingua, DatiOrdine dati, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);
        // Un deadlock fra ordini concorrenti annulla l'intera transazione (carrello, numero, prenotazioni): si riprova da capo.
        return RiprovaSuDeadlock.EseguiAsync(_contesto, () => CreaDaCarrelloUnaVoltaAsync(idAccount, codiceLingua, dati, annullamento));
    }

    private async Task<EsitoOperazione> CreaDaCarrelloUnaVoltaAsync(Guid idAccount, string codiceLingua, DatiOrdine dati, CancellationToken annullamento)
    {

        var account = await TrovaAccountAsync(idAccount, annullamento);
        if (account is null)
        {
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idTenant = _tenant.IdTenant;
        var idSito = _tenant.IdSito;
        if (!await _contesto.Siti.AsNoTracking().AnyAsync(sito => sito.Id == idSito && sito.CommercioAttivo, annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var metodo = await _contesto.MetodiSpedizione.AsNoTracking().SingleOrDefaultAsync(m => m.Id == dati.IdMetodoSpedizione && m.IdSito == idSito && m.Attivo, annullamento);
        if (metodo is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var indirizzo = ValidaIndirizzo(metodo, dati, out var campiNonValidi);
        if (campiNonValidi.Count > 0)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, campiNonValidi);
        }

        // Le prenotazioni scadute non devono togliere merce a chi compra adesso.
        await _giacenze.RilasciaScaduteAsync(annullamento);

        // Prezzi, aliquote e disponibilità vengono sempre dal catalogo attuale, mai dal cliente.
        var vista = await _carrello.LeggiAsync(idAccount, codiceLingua, annullamento);
        if (vista.Righe.Count == 0 && vista.NonDisponibili.Count == 0)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Carrello"]);
        }

        if (vista.NonDisponibili.Count > 0)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto, null, ["Carrello"]);
        }

        var modalita = dati.ModalitaPagamento;
        if (!Enum.IsDefined(modalita))
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["ModalitaPagamento"]);
        }

        var spedizione = CalcolatoreTotali.CalcolaSpedizione(metodo.TariffaLorda);
        var righeCalcolate = vista.Righe.Select(r => CalcolatoreTotali.CalcolaRiga(r.PrezzoUnitarioLordo, r.Quantita, r.Aliquota)).ToList();
        var senzaCommissione = CalcolatoreTotali.Somma([.. righeCalcolate, spedizione]);

        // Bonifico e contrassegno solo se il tenant li offre; il contrassegno aggiunge la sua commissione ai totali (calcolata qui, mai dal cliente).
        var offline = await LeggiPagamentiOfflineAsync(idSito, annullamento);
        var preventivo = PreventivoPagamento.Calcola(offline, modalita, metodo.Tipo, metodo.Area, senzaCommissione.Lordo);
        if (!preventivo.Disponibile)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["ModalitaPagamento"]);
        }

        var commissione = CalcolatoreTotali.CalcolaCommissione(preventivo.Commissione);
        var totali = CalcolatoreTotali.Somma([.. righeCalcolate, spedizione, commissione]);
        if (totali.Lordo != dati.TotaleAtteso)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto, null, ["Totale"]);
        }

        var lingueSito = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var ordine = new Ordine
        {
            Id = Identificativi.Nuovo(_orologio),
            IdTenant = idTenant,
            IdSito = idSito,
            IdUtenza = account.Id,
            Stato = modalita == ModalitaPagamento.Contrassegno ? StatoOrdine.DaSpedireContrassegno : StatoOrdine.InAttesaDiPagamento,
            ModalitaPagamento = modalita,
            CodiceLingua = lingueSito.FirstOrDefault(l => string.Equals(l.Codice, codiceLingua, StringComparison.OrdinalIgnoreCase))?.Codice
                ?? lingueSito.FirstOrDefault(l => l.Predefinita)?.Codice
                ?? "it",
            Valuta = vista.Valuta,
            TotaleLordo = totali.Lordo,
            TotaleImponibile = totali.Imponibile,
            TotaleIva = totali.Iva,
            TipoSpedizione = metodo.Tipo,
            AreaSpedizione = metodo.Area,
            NomeSpedizione = metodo.Nome,
            SpedizioneLorda = spedizione.Lordo,
            SpedizioneImponibile = spedizione.Imponibile,
            SpedizioneIva = spedizione.Iva,
            CommissioneLorda = commissione.Lordo,
            CommissioneImponibile = commissione.Imponibile,
            CommissioneIva = commissione.Iva,
            EmailAcquirente = account.Email,
            NomeAcquirente = account.NomeVisualizzato,
            Destinatario = indirizzo?.Destinatario,
            IndirizzoRiga1 = indirizzo?.Riga1,
            IndirizzoRiga2 = indirizzo?.Riga2,
            Cap = indirizzo?.Cap,
            Citta = indirizzo?.Citta,
            Paese = indirizzo?.Paese,
            CreatoIlUtc = adesso,
            AggiornatoIlUtc = adesso,
        };

        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);

        // Svuota il carrello per primo: chi invia due volte la stessa cassa trova le righe già tolte e il secondo ordine non nasce.
        var attese = vista.Righe.Count + vista.NonDisponibili.Count;
        var carrello = await _contesto.Carrelli.AsNoTracking().SingleOrDefaultAsync(c => c.IdSito == idSito && c.IdUtenza == account.Id, annullamento);
        var tolte = carrello is null ? 0 : await _contesto.RigheCarrello.Where(r => r.IdCarrello == carrello.Id).ExecuteDeleteAsync(annullamento);
        if (tolte != attese)
        {
            await transazione.RollbackAsync(annullamento);
            return new EsitoOperazione(CodiceEsito.Conflitto, null, ["Carrello"]);
        }

        // Il numero progressivo si prende con un aggiornamento che blocca la riga del sito fino alla fine della transazione.
        await _contesto.Siti.Where(s => s.Id == idSito).ExecuteUpdateAsync(s => s.SetProperty(x => x.UltimoNumeroOrdine, x => x.UltimoNumeroOrdine + 1), annullamento);
        ordine.Numero = await _contesto.Siti.AsNoTracking().Where(s => s.Id == idSito).Select(s => s.UltimoNumeroOrdine).SingleAsync(annullamento);

        // Prenotazione atomica della giacenza gestita. Dura 15 minuti per il pagamento online, i giorni impostati dal tenant per il bonifico,
        // e finché l'ordine non si spedisce o si annulla per il contrassegno. Se manca anche una sola quantità non nasce nessun ordine.
        var scade = modalita switch
        {
            ModalitaPagamento.Bonifico => adesso.AddDays(offline.GiorniPerPagare),
            ModalitaPagamento.Contrassegno => PrenotazioneGiacenza.SenzaScadenza,
            _ => adesso.AddMinutes(PrenotazioneGiacenza.DurataMinuti),
        };
        var prenotazione = await OperazioniGiacenza.PrenotaAsync(
            _contesto,
            ordine,
            vista.Righe.Select(r => (r.IdVariante, r.Quantita)),
            adesso,
            scade,
            () => Identificativi.Nuovo(_orologio),
            annullamento);
        if (prenotazione.Insufficienti.Count > 0)
        {
            await transazione.RollbackAsync(annullamento);
            _contesto.ChangeTracker.Clear();
            var mancanti = vista.Righe.Where(r => prenotazione.Insufficienti.Contains(r.IdVariante)).Select(r => $"Giacenza:{r.Sku}");
            return new EsitoOperazione(CodiceEsito.Conflitto, null, [.. mancanti]);
        }

        ordine.PrenotazioneScadeIlUtc = prenotazione.Prenotate > 0 && modalita != ModalitaPagamento.Contrassegno ? scade : null;
        _contesto.Ordini.Add(ordine);
        var posizione = 0;
        foreach (var (vistaRiga, calcolata) in vista.Righe.Zip(totali.Righe))
        {
            _contesto.RigheOrdine.Add(new RigaOrdine
            {
                Id = Identificativi.Nuovo(_orologio),
                IdTenant = idTenant,
                IdSito = idSito,
                IdOrdine = ordine.Id,
                Posizione = posizione++,
                IdVariante = vistaRiga.IdVariante,
                Sku = vistaRiga.Sku,
                NomeProdotto = vistaRiga.NomeProdotto,
                EtichettaVariante = vistaRiga.EtichettaVariante,
                Quantita = calcolata.Quantita,
                PrezzoUnitarioLordo = calcolata.PrezzoUnitarioLordo,
                AliquotaIva = calcolata.Aliquota,
                Lordo = calcolata.Lordo,
                Imponibile = calcolata.Imponibile,
                Iva = calcolata.Iva,
            });
        }

        await _contesto.SaveChangesAsync(annullamento);

        // Con un pagamento manuale il cliente riceve subito le istruzioni e il tenant l'avviso: nella stessa transazione dell'ordine (o tutto o niente).
        if (modalita != ModalitaPagamento.Online)
        {
            await _notifiche.AccodaOrdineRicevutoAsync(ordine, offline, annullamento);
            if (_opzioni.AvvisoNuovoOrdineAmministratori)
            {
                await _notifiche.AccodaAvvisoAmministratoriAsync(ordine, annullamento);
            }

            await _contesto.SaveChangesAsync(annullamento);
        }

        await transazione.CommitAsync(annullamento);

        await _audit.RegistraAsync(
            AzioniAudit.CreazioneOrdine,
            $"Ordine:{ordine.Id}",
            RegistroAudit.EsitoRiuscito,
            idTenant,
            account.Id,
            $"numero {ordine.Numero}, {vista.Righe.Count} righe, totale {ordine.TotaleLordo:0.00} {ordine.Valuta}, pagamento {modalita}",
            annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, ordine.Id);
    }

    public async Task<IReadOnlyList<RiepilogoOrdine>> ElencaMieiAsync(Guid idAccount, CancellationToken annullamento)
    {
        if (await TrovaAccountAsync(idAccount, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli account autenticati del tenant.");
        }

        var idSito = _tenant.IdSito;
        return await Riepiloghi(_contesto.Ordini.AsNoTracking().Where(o => o.IdSito == idSito && o.IdUtenza == idAccount)).ToListAsync(annullamento);
    }

    public async Task<OrdineVista?> LeggiMioAsync(Guid idAccount, Guid idOrdine, CancellationToken annullamento)
    {
        if (await TrovaAccountAsync(idAccount, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli account autenticati del tenant.");
        }

        var idSito = _tenant.IdSito;
        var ordine = await _contesto.Ordini.AsNoTracking().SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == idSito && o.IdUtenza == idAccount, annullamento);
        return ordine is null ? null : await VistaAsync(ordine, annullamento);
    }

    public async Task<EsitoOperazione> AnnullaMioAsync(Guid idAccount, Guid idOrdine, CancellationToken annullamento)
    {
        var account = await TrovaAccountAsync(idAccount, annullamento);
        if (account is null)
        {
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idSito = _tenant.IdSito;
        var ordine = await _contesto.Ordini.SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == idSito && o.IdUtenza == idAccount, annullamento);
        if (ordine is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        if (!TransizioniOrdine.AnnullabileDalCliente(ordine.Stato) || !TransizioniOrdine.Consentita(ordine.Stato, StatoOrdine.Annullato, ordine.TipoSpedizione, ordine.ModalitaPagamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var precedente = ordine.Stato;
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        ordine.Stato = StatoOrdine.Annullato;
        ordine.AggiornatoIlUtc = adesso;
        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
        try
        {
            await _contesto.SaveChangesAsync(annullamento);
            await OperazioniGiacenza.RilasciaAsync(_contesto, _tenant.IdTenant, ordine.Id, adesso, annullamento);
            await transazione.CommitAsync(annullamento);
        }
        catch (DbUpdateConcurrencyException)
        {
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        await _audit.RegistraAsync(
            AzioniAudit.AnnullamentoOrdine,
            $"Ordine:{ordine.Id}",
            RegistroAudit.EsitoRiuscito,
            _tenant.IdTenant,
            account.Id,
            $"numero {ordine.Numero}, da {precedente}",
            annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, ordine.Id);
    }

    public async Task<IReadOnlyList<RiepilogoOrdine>> ElencaAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        var idSito = _tenant.IdSito;
        return await Riepiloghi(_contesto.Ordini.AsNoTracking().Where(o => o.IdSito == idSito)).Take(LimiteElenco).ToListAsync(annullamento);
    }

    public async Task<OrdineVista?> LeggiAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        var idSito = _tenant.IdSito;
        var ordine = await _contesto.Ordini.AsNoTracking().SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == idSito, annullamento);
        return ordine is null ? null : await VistaAsync(ordine, annullamento);
    }

    public async Task<EsitoOperazione> SegnaEvasoAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAmministrazioneAsync(idAttore, annullamento);
        if (attore is null)
        {
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idTenant = _tenant.IdTenant;
        var idSito = _tenant.IdSito;
        var ordine = await _contesto.Ordini.AsNoTracking().SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == idSito, annullamento);
        if (ordine is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var destinazione = ordine.TipoSpedizione == TipoMetodoSpedizione.Zona ? StatoOrdine.Spedito : StatoOrdine.Ritirato;
        var daContrassegno = ordine.Stato == StatoOrdine.DaSpedireContrassegno;
        if ((ordine.Stato != StatoOrdine.Pagato && !daContrassegno) || !TransizioniOrdine.Consentita(ordine.Stato, destinazione, ordine.TipoSpedizione, ordine.ModalitaPagamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var partenza = ordine.Stato;
        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
        var cambiati = await _contesto.Ordini
            .Where(o => o.Id == idOrdine && o.Stato == partenza)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, destinazione).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
        if (cambiati == 0)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        // Il contrassegno trattiene la merce fino alla spedizione: ora esce dal magazzino.
        if (daContrassegno && !await OperazioniGiacenza.ConfermaAsync(_contesto, idTenant, idSito, idOrdine, adesso, () => Identificativi.Nuovo(_orologio), annullamento))
        {
            await transazione.RollbackAsync(annullamento);
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        await _contesto.SaveChangesAsync(annullamento);
        await transazione.CommitAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.EvasioneOrdine, $"Ordine:{idOrdine}", RegistroAudit.EsitoRiuscito, idTenant, attore.Id, $"numero {ordine.Numero}: {destinazione}", annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idOrdine);
    }

    public async Task<EsitoOperazione> SegnaPagatoAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAmministrazioneAsync(idAttore, annullamento);
        if (attore is null)
        {
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idTenant = _tenant.IdTenant;
        var idSito = _tenant.IdSito;
        var ordine = await _contesto.Ordini.AsNoTracking().SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == idSito, annullamento);
        if (ordine is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        if (ordine.ModalitaPagamento != ModalitaPagamento.Bonifico || !TransizioniOrdine.Consentita(ordine.Stato, StatoOrdine.Pagato, ordine.TipoSpedizione, ordine.ModalitaPagamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
        var cambiati = await _contesto.Ordini
            .Where(o => o.Id == idOrdine && o.Stato == StatoOrdine.InAttesaDiPagamento && o.ModalitaPagamento == ModalitaPagamento.Bonifico)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, StatoOrdine.Pagato).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
        if (cambiati == 0)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        // La merce prenotata esce dal magazzino; se non è più garantita (prenotazione rilasciata) l'ordine non risulta pagato.
        if (!await OperazioniGiacenza.ConfermaAsync(_contesto, idTenant, idSito, idOrdine, adesso, () => Identificativi.Nuovo(_orologio), annullamento))
        {
            await transazione.RollbackAsync(annullamento);
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        await _notifiche.AccodaConfermaPagamentoAsync(ordine, annullamento);
        await _contesto.SaveChangesAsync(annullamento);
        await transazione.CommitAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.PagamentoOfflineRicevuto, $"Ordine:{idOrdine}", RegistroAudit.EsitoRiuscito, idTenant, attore.Id, $"numero {ordine.Numero}: bonifico ricevuto", annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idOrdine);
    }

    public async Task<EsitoOperazione> AnnullaAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAmministrazioneAsync(idAttore, annullamento);
        if (attore is null)
        {
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idTenant = _tenant.IdTenant;
        var idSito = _tenant.IdSito;
        var ordine = await _contesto.Ordini.AsNoTracking().SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == idSito, annullamento);
        if (ordine is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        // Solo i pagamenti manuali non ancora incassati: un ordine online ha un provider che può aver incassato, e si chiude da solo.
        var annullabile = ordine.ModalitaPagamento != ModalitaPagamento.Online
            && ordine.Stato is StatoOrdine.InAttesaDiPagamento or StatoOrdine.DaSpedireContrassegno
            && TransizioniOrdine.Consentita(ordine.Stato, StatoOrdine.Annullato, ordine.TipoSpedizione, ordine.ModalitaPagamento);
        if (!annullabile)
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var partenza = ordine.Stato;
        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
        var cambiati = await _contesto.Ordini
            .Where(o => o.Id == idOrdine && o.Stato == partenza)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, StatoOrdine.Annullato).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
        if (cambiati == 0)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        await OperazioniGiacenza.RilasciaAsync(_contesto, idTenant, idOrdine, adesso, annullamento);
        await transazione.CommitAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.AnnullamentoOrdineAmministrazione, $"Ordine:{idOrdine}", RegistroAudit.EsitoRiuscito, idTenant, attore.Id, $"numero {ordine.Numero}, da {partenza}", annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idOrdine);
    }

    public async Task<EsitoOperazione> SegnaConsegnaNonRiuscitaAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAmministrazioneAsync(idAttore, annullamento);
        if (attore is null)
        {
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idSito = _tenant.IdSito;
        var ordine = await _contesto.Ordini.AsNoTracking().SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == idSito, annullamento);
        if (ordine is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        if (ordine.ModalitaPagamento != ModalitaPagamento.Contrassegno || ordine.Stato != StatoOrdine.Spedito || !TransizioniOrdine.Consentita(ordine.Stato, StatoOrdine.Annullato, ordine.TipoSpedizione, ordine.ModalitaPagamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var cambiati = await _contesto.Ordini
            .Where(o => o.Id == idOrdine && o.Stato == StatoOrdine.Spedito && o.ModalitaPagamento == ModalitaPagamento.Contrassegno)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, StatoOrdine.Annullato).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
        if (cambiati == 0)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        await _audit.RegistraAsync(AzioniAudit.ConsegnaNonRiuscita, $"Ordine:{idOrdine}", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, attore.Id, $"numero {ordine.Numero}: consegna in contrassegno non riuscita", annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idOrdine);
    }

    private Task<Utenza?> TrovaAttoreAmministrazioneAsync(Guid idAttore, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin || (candidata.Ruolo == RuoloUtenza.TenantAdmin && candidata.IdTenant == idTenant)),
            annullamento);
    }

    private async Task<ImpostazioniPagamentoOffline> LeggiPagamentiOfflineAsync(Guid idSito, CancellationToken annullamento)
    {
        var riga = await _contesto.ImpostazioniPagamentoOfflineSiti.AsNoTracking().SingleOrDefaultAsync(i => i.IdSito == idSito, annullamento);
        return riga is null ? ImpostazioniPagamentoOffline.Spente : ServizioPagamentiOffline.Vista(riga);
    }

    private static IQueryable<RiepilogoOrdine> Riepiloghi(IQueryable<Ordine> ordini) =>
        ordini.OrderByDescending(o => o.Numero).Select(o => new RiepilogoOrdine(o.Id, o.Numero, o.Stato, o.TotaleLordo, o.Valuta, o.CreatoIlUtc, o.EmailAcquirente, o.ModalitaPagamento));

    private async Task<OrdineVista> VistaAsync(Ordine o, CancellationToken annullamento)
    {
        var righe = await _contesto.RigheOrdine.AsNoTracking().Where(r => r.IdOrdine == o.Id).OrderBy(r => r.Posizione).ToListAsync(annullamento);
        return new OrdineVista(
            o.Id,
            o.Numero,
            o.Stato,
            o.Valuta,
            o.TotaleLordo,
            o.TotaleImponibile,
            o.TotaleIva,
            o.TotaleRimborsato,
            o.TipoSpedizione,
            o.AreaSpedizione,
            o.NomeSpedizione,
            o.SpedizioneLorda,
            o.SpedizioneImponibile,
            o.SpedizioneIva,
            o.EmailAcquirente,
            o.NomeAcquirente,
            o.Destinatario,
            o.IndirizzoRiga1,
            o.IndirizzoRiga2,
            o.Cap,
            o.Citta,
            o.Paese,
            o.CreatoIlUtc,
            o.AggiornatoIlUtc,
            o.PrenotazioneScadeIlUtc,
            [.. righe.Select(r => new RigaOrdineVista(r.Posizione, r.Sku, r.NomeProdotto, r.EtichettaVariante, r.Quantita, r.PrezzoUnitarioLordo, r.AliquotaIva, r.Lordo, r.Imponibile, r.Iva))],
            o.GiacenzaReinseritaIlUtc,
            o.ModalitaPagamento,
            o.CommissioneLorda,
            o.CommissioneImponibile,
            o.CommissioneIva);
    }

    private sealed record Indirizzo(string Destinatario, string Riga1, string? Riga2, string Cap, string Citta, string Paese);

    /// <summary>Il ritiro non ha indirizzo (quello inviato si scarta); la consegna lo richiede e il paese deve stare nell'area del metodo.</summary>
    private static Indirizzo? ValidaIndirizzo(MetodoSpedizione metodo, DatiOrdine dati, out List<string> nonValidi)
    {
        nonValidi = [];
        if (metodo.Tipo == TipoMetodoSpedizione.Ritiro)
        {
            return null;
        }

        var destinatario = dati.Destinatario?.Trim() ?? string.Empty;
        var riga1 = dati.IndirizzoRiga1?.Trim() ?? string.Empty;
        var riga2 = dati.IndirizzoRiga2?.Trim() ?? string.Empty;
        var cap = dati.Cap?.Trim() ?? string.Empty;
        var citta = dati.Citta?.Trim() ?? string.Empty;
        var paese = dati.Paese?.Trim().ToUpperInvariant() ?? string.Empty;

        if (!TestoValido(destinatario, Ordine.LunghezzaMassimaDestinatario, obbligatorio: true))
        {
            nonValidi.Add("Destinatario");
        }

        if (!TestoValido(riga1, Ordine.LunghezzaMassimaIndirizzo, obbligatorio: true))
        {
            nonValidi.Add("IndirizzoRiga1");
        }

        if (!TestoValido(riga2, Ordine.LunghezzaMassimaIndirizzo, obbligatorio: false))
        {
            nonValidi.Add("IndirizzoRiga2");
        }

        if (!FormatoCap().IsMatch(cap))
        {
            nonValidi.Add("Cap");
        }

        if (!TestoValido(citta, Ordine.LunghezzaMassimaCitta, obbligatorio: true))
        {
            nonValidi.Add("Citta");
        }

        if (metodo.Area is not { } area || !PaesiSpedizione.Appartiene(area, paese))
        {
            nonValidi.Add("Paese");
        }

        return nonValidi.Count > 0 ? null : new Indirizzo(destinatario, riga1, riga2.Length == 0 ? null : riga2, cap, citta, paese);
    }

    private static bool TestoValido(string testo, int lunghezzaMassima, bool obbligatorio) =>
        (testo.Length > 0 || !obbligatorio) && testo.Length <= lunghezzaMassima && !testo.Any(char.IsControl);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 -]{0,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoCap();

    private async Task RichiediAttoreAsync(Guid idAttore, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        var autorizzato = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin
                    || (candidata.Ruolo == RuoloUtenza.TenantAdmin && candidata.IdTenant == idTenant)),
            annullamento);
        if (!autorizzato)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }
    }

    /// <summary>Può ordinare chi ha un account attivo: <c>Utente</c> o <c>TenantAdmin</c> del tenant dell'host, <c>PlatformAdmin</c> ovunque (ADR-0011).</summary>
    private Task<Utenza?> TrovaAccountAsync(Guid idAccount, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAccount
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin
                    || (candidata.IdTenant == idTenant && (candidata.Ruolo == RuoloUtenza.Utente || candidata.Ruolo == RuoloUtenza.TenantAdmin))),
            annullamento);
    }
}
