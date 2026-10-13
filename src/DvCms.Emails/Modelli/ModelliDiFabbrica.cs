using DvCms.DTO.Email;

namespace DvCms.Emails;

/// <summary>
/// I modelli di fabbrica, in italiano e in inglese (ADR-0025). Sono codice, non dati: seguono la versione del prodotto e il tenant
/// li vede come punto di partenza da copiare. Il testo italiano riprende le frasi che prima stavano nel codice.
/// </summary>
internal static class ModelliDiFabbrica
{
    public static IReadOnlyDictionary<(string Codice, string Lingua), ContenutoModelloEmail> Contenuti { get; } = Costruisci();

    private static string Pagina(string corpo) =>
        "<!DOCTYPE html>\n<html>\n<body style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#222222\">\n" + corpo + "\n</body>\n</html>";

    private static Dictionary<(string, string), ContenutoModelloEmail> Costruisci()
    {
        var contenuti = new Dictionary<(string, string), ContenutoModelloEmail>();

        void Aggiungi(string codice, string lingua, string oggetto, string html, string testo) =>
            contenuti[(codice, lingua)] = new ContenutoModelloEmail(oggetto, Pagina(html), testo);

        // ---- Italiano ----
        const string it = "it";
        Aggiungi(
            CodiciModelloEmail.RecuperoCodice, it,
            "Codice per reimpostare la password",
            "<p>Il tuo codice di verifica è <strong>{{ codice }}</strong>. Vale {{ minuti_validita }} minuti.</p>\n<p>Se non hai chiesto tu di reimpostare la password, ignora questo messaggio.</p>",
            "Il tuo codice di verifica è {{ codice }}. Vale {{ minuti_validita }} minuti. Se non hai chiesto tu di reimpostare la password, ignora questo messaggio.");
        Aggiungi(
            CodiciModelloEmail.RecuperoLink, it,
            "Link per reimpostare la password",
            "<p>Per scegliere una nuova password apri questo link entro {{ minuti_validita }} minuti:</p>\n<p><a href=\"{{ link }}\">{{ link }}</a></p>\n<p>Il link si può usare una sola volta. Se non sei stato tu, ignora questo messaggio.</p>",
            "Per scegliere una nuova password apri questo link entro {{ minuti_validita }} minuti: {{ link }}. Il link si può usare una sola volta. Se non sei stato tu, ignora questo messaggio.");
        Aggiungi(
            CodiciModelloEmail.RecuperoNotifica, it,
            "La tua password è stata cambiata",
            "<p>La password del tuo account è stata appena cambiata.</p>\n<p>Se non sei stato tu, contatta subito l'amministratore del sito.</p>",
            "La password del tuo account è stata appena cambiata. Se non sei stato tu, contatta subito l'amministratore del sito.");
        Aggiungi(
            CodiciModelloEmail.ConfermaRegistrazione, it,
            "Conferma la tua registrazione a {{ nome_sito }}",
            "<p>Per completare la registrazione a {{ nome_sito }} apri questo link entro {{ ore_validita }} ore e scegli la tua password:</p>\n<p><a href=\"{{ link }}\">{{ link }}</a></p>\n<p>Il link si può usare una sola volta. Se non sei stato tu, ignora questo messaggio.</p>",
            "Per completare la registrazione a {{ nome_sito }} apri questo link entro {{ ore_validita }} ore e scegli la tua password: {{ link }}. Il link si può usare una sola volta. Se non sei stato tu, ignora questo messaggio.");
        Aggiungi(
            CodiciModelloEmail.InvitoAmministratore, it,
            "Invito come amministratore di {{ nome_tenant }}",
            "<p>Sei stato invitato a gestire il sito {{ nome_tenant }}. Per scegliere la tua password apri questo link entro {{ ore_validita }} ore:</p>\n<p><a href=\"{{ link }}\">{{ link }}</a></p>\n<p>Il link si può usare una sola volta.</p>",
            "Sei stato invitato a gestire il sito {{ nome_tenant }}. Per scegliere la tua password apri questo link entro {{ ore_validita }} ore: {{ link }}. Il link si può usare una sola volta.");

        const string dettaglioHtmlIt =
            "<ul>{% for riga in righe %}<li>{{ riga.nome }} (codice {{ riga.codice }}) × {{ riga.quantita }}: {{ riga.importo }}</li>{% endfor %}</ul>\n"
            + "<p>Consegna ({{ consegna_nome }}): {{ consegna_importo }}<br>{% if commissione %}Commissione contrassegno: {{ commissione }}<br>{% endif %}<strong>Totale (IVA inclusa): {{ totale }}</strong></p>\n"
            + "{% if ritiro %}<p>Ritiro in sede.</p>{% else %}<p>Consegna a:<br>{% for riga in indirizzo %}{{ riga }}<br>{% endfor %}</p>{% endif %}\n"
            + "<p>Puoi consultare l'ordine qui: <a href=\"{{ link_ordine }}\">{{ link_ordine }}</a></p>";
        const string dettaglioTestoIt =
            "{% for riga in righe %}- {{ riga.nome }} (codice {{ riga.codice }}) × {{ riga.quantita }}: {{ riga.importo }}\n{% endfor %}"
            + "Consegna ({{ consegna_nome }}): {{ consegna_importo }}\n"
            + "{% if commissione %}Commissione contrassegno: {{ commissione }}\n{% endif %}"
            + "Totale (IVA inclusa): {{ totale }}\n\n"
            + "{% if ritiro %}Ritiro in sede.{% else %}Consegna a:\n{% for riga in indirizzo %}{{ riga }}\n{% endfor %}{% endif %}\n\n"
            + "Puoi consultare l'ordine qui: {{ link_ordine }}";

        Aggiungi(
            CodiciModelloEmail.OrdineConfermato, it,
            "Ordine n. {{ numero }} confermato - {{ nome_sito }}",
            "<p>Grazie per il tuo ordine n. {{ numero }} su {{ nome_sito }}: abbiamo ricevuto il pagamento.</p>\n" + dettaglioHtmlIt,
            "Grazie per il tuo ordine n. {{ numero }} su {{ nome_sito }}: abbiamo ricevuto il pagamento.\n\n" + dettaglioTestoIt);
        Aggiungi(
            CodiciModelloEmail.OrdineRicevutoBonifico, it,
            "Ordine n. {{ numero }} ricevuto: istruzioni per il bonifico - {{ nome_sito }}",
            "<p>Grazie per il tuo ordine n. {{ numero }} su {{ nome_sito }}. Per completarlo, effettua un bonifico con questi dati:</p>\n"
            + "<p>Importo: {{ importo }}<br>Intestatario: {{ intestatario }}<br>IBAN: {{ iban }}<br>{% if bic %}BIC/SWIFT: {{ bic }}<br>{% endif %}{% if banca %}Banca: {{ banca }}<br>{% endif %}Causale: {{ causale }}</p>\n"
            + "{% if scadenza %}<p>Effettua il bonifico entro il {{ scadenza }}: dopo questa data l'ordine viene annullato.</p>\n{% endif %}"
            + "{% if istruzioni %}<p>{{ istruzioni | escape | newline_to_br | raw }}</p>\n{% endif %}"
            + "<p>Appena riceviamo il pagamento ti scriviamo per confermare l'ordine.</p>\n" + dettaglioHtmlIt,
            "Grazie per il tuo ordine n. {{ numero }} su {{ nome_sito }}. Per completarlo, effettua un bonifico con questi dati:\n\n"
            + "Importo: {{ importo }}\nIntestatario: {{ intestatario }}\nIBAN: {{ iban }}\n"
            + "{% if bic %}BIC/SWIFT: {{ bic }}\n{% endif %}{% if banca %}Banca: {{ banca }}\n{% endif %}Causale: {{ causale }}\n"
            + "{% if scadenza %}Effettua il bonifico entro il {{ scadenza }}: dopo questa data l'ordine viene annullato.\n{% endif %}"
            + "{% if istruzioni %}\n{{ istruzioni }}\n{% endif %}\n"
            + "Appena riceviamo il pagamento ti scriviamo per confermare l'ordine.\n\n" + dettaglioTestoIt);
        Aggiungi(
            CodiciModelloEmail.OrdineRicevutoContrassegno, it,
            "Ordine n. {{ numero }} ricevuto - {{ nome_sito }}",
            "<p>Grazie per il tuo ordine n. {{ numero }} su {{ nome_sito }}: lo abbiamo ricevuto e lo prepariamo per la spedizione.</p>\n"
            + "<p>Pagherai {{ importo }} alla consegna (contrassegno).</p>\n"
            + "{% if istruzioni %}<p>{{ istruzioni | escape | newline_to_br | raw }}</p>\n{% endif %}" + dettaglioHtmlIt,
            "Grazie per il tuo ordine n. {{ numero }} su {{ nome_sito }}: lo abbiamo ricevuto e lo prepariamo per la spedizione.\n"
            + "Pagherai {{ importo }} alla consegna (contrassegno).\n"
            + "{% if istruzioni %}{{ istruzioni }}\n{% endif %}\n" + dettaglioTestoIt);
        Aggiungi(
            CodiciModelloEmail.OrdineNuovoAmministratore, it,
            "Nuovo ordine n. {{ numero }} - {{ nome_sito }}",
            "<p>{% if tipo == 'bonifico' %}Nuovo ordine n. {{ numero }} su {{ nome_sito }} in attesa di bonifico: {{ importo }}, {{ articoli }} articoli."
            + "{% elsif tipo == 'contrassegno' %}Nuovo ordine n. {{ numero }} su {{ nome_sito }} in contrassegno, da spedire: {{ importo }}, {{ articoli }} articoli."
            + "{% else %}Nuovo ordine pagato n. {{ numero }} su {{ nome_sito }}: {{ importo }}, {{ articoli }} articoli.{% endif %}</p>\n"
            + "<p>Cliente: {{ cliente_nome }} ({{ cliente_email }})<br>Consegna: {{ consegna_nome }}</p>\n"
            + "<p>Gestisci l'ordine qui: <a href=\"{{ link_ordine }}\">{{ link_ordine }}</a></p>",
            "{% if tipo == 'bonifico' %}Nuovo ordine n. {{ numero }} su {{ nome_sito }} in attesa di bonifico: {{ importo }}, {{ articoli }} articoli."
            + "{% elsif tipo == 'contrassegno' %}Nuovo ordine n. {{ numero }} su {{ nome_sito }} in contrassegno, da spedire: {{ importo }}, {{ articoli }} articoli."
            + "{% else %}Nuovo ordine pagato n. {{ numero }} su {{ nome_sito }}: {{ importo }}, {{ articoli }} articoli.{% endif %}\n"
            + "Cliente: {{ cliente_nome }} ({{ cliente_email }})\nConsegna: {{ consegna_nome }}\nGestisci l'ordine qui: {{ link_ordine }}");
        Aggiungi(
            CodiciModelloEmail.Rimborso, it,
            "Rimborso per l'ordine n. {{ numero }} - {{ nome_sito }}",
            "<p>{% if totale %}Abbiamo rimborsato per intero l'ordine n. {{ numero }} su {{ nome_sito }}: {{ importo }}.{% else %}Abbiamo rimborsato una parte dell'ordine n. {{ numero }} su {{ nome_sito }}: {{ importo }}.{% endif %}</p>\n"
            + "{% if motivo %}<p>Motivo: {{ motivo }}</p>\n{% endif %}"
            + "<p>L'accredito sul metodo di pagamento che hai usato può richiedere alcuni giorni.</p>\n"
            + "<p>Puoi consultare l'ordine qui: <a href=\"{{ link_ordine }}\">{{ link_ordine }}</a></p>",
            "{% if totale %}Abbiamo rimborsato per intero l'ordine n. {{ numero }} su {{ nome_sito }}: {{ importo }}.{% else %}Abbiamo rimborsato una parte dell'ordine n. {{ numero }} su {{ nome_sito }}: {{ importo }}.{% endif %}\n"
            + "{% if motivo %}Motivo: {{ motivo }}\n{% endif %}"
            + "L'accredito sul metodo di pagamento che hai usato può richiedere alcuni giorni.\nPuoi consultare l'ordine qui: {{ link_ordine }}");
        Aggiungi(
            CodiciModelloEmail.Prova, it,
            "Email di prova",
            "<p>Questa è un'email di prova inviata dalla configurazione del tenant. Se la ricevi, l'invio funziona.</p>",
            "Questa è un'email di prova inviata dalla configurazione del tenant. Se la ricevi, l'invio funziona.");

        // ---- Inglese ----
        const string en = "en";
        Aggiungi(
            CodiciModelloEmail.RecuperoCodice, en,
            "Code to reset your password",
            "<p>Your verification code is <strong>{{ codice }}</strong>. It is valid for {{ minuti_validita }} minutes.</p>\n<p>If you did not ask to reset your password, ignore this message.</p>",
            "Your verification code is {{ codice }}. It is valid for {{ minuti_validita }} minutes. If you did not ask to reset your password, ignore this message.");
        Aggiungi(
            CodiciModelloEmail.RecuperoLink, en,
            "Link to reset your password",
            "<p>To choose a new password open this link within {{ minuti_validita }} minutes:</p>\n<p><a href=\"{{ link }}\">{{ link }}</a></p>\n<p>The link can be used only once. If it was not you, ignore this message.</p>",
            "To choose a new password open this link within {{ minuti_validita }} minutes: {{ link }}. The link can be used only once. If it was not you, ignore this message.");
        Aggiungi(
            CodiciModelloEmail.RecuperoNotifica, en,
            "Your password has been changed",
            "<p>The password of your account has just been changed.</p>\n<p>If it was not you, contact the site administrator right away.</p>",
            "The password of your account has just been changed. If it was not you, contact the site administrator right away.");
        Aggiungi(
            CodiciModelloEmail.ConfermaRegistrazione, en,
            "Confirm your registration to {{ nome_sito }}",
            "<p>To complete your registration to {{ nome_sito }} open this link within {{ ore_validita }} hours and choose your password:</p>\n<p><a href=\"{{ link }}\">{{ link }}</a></p>\n<p>The link can be used only once. If it was not you, ignore this message.</p>",
            "To complete your registration to {{ nome_sito }} open this link within {{ ore_validita }} hours and choose your password: {{ link }}. The link can be used only once. If it was not you, ignore this message.");
        Aggiungi(
            CodiciModelloEmail.InvitoAmministratore, en,
            "Invitation to administer {{ nome_tenant }}",
            "<p>You have been invited to manage the site {{ nome_tenant }}. To choose your password open this link within {{ ore_validita }} hours:</p>\n<p><a href=\"{{ link }}\">{{ link }}</a></p>\n<p>The link can be used only once.</p>",
            "You have been invited to manage the site {{ nome_tenant }}. To choose your password open this link within {{ ore_validita }} hours: {{ link }}. The link can be used only once.");

        const string dettaglioHtmlEn =
            "<ul>{% for riga in righe %}<li>{{ riga.nome }} (code {{ riga.codice }}) × {{ riga.quantita }}: {{ riga.importo }}</li>{% endfor %}</ul>\n"
            + "<p>Delivery ({{ consegna_nome }}): {{ consegna_importo }}<br>{% if commissione %}Cash on delivery fee: {{ commissione }}<br>{% endif %}<strong>Total (VAT included): {{ totale }}</strong></p>\n"
            + "{% if ritiro %}<p>Pick-up at the store.</p>{% else %}<p>Delivery to:<br>{% for riga in indirizzo %}{{ riga }}<br>{% endfor %}</p>{% endif %}\n"
            + "<p>You can see your order here: <a href=\"{{ link_ordine }}\">{{ link_ordine }}</a></p>";
        const string dettaglioTestoEn =
            "{% for riga in righe %}- {{ riga.nome }} (code {{ riga.codice }}) × {{ riga.quantita }}: {{ riga.importo }}\n{% endfor %}"
            + "Delivery ({{ consegna_nome }}): {{ consegna_importo }}\n"
            + "{% if commissione %}Cash on delivery fee: {{ commissione }}\n{% endif %}"
            + "Total (VAT included): {{ totale }}\n\n"
            + "{% if ritiro %}Pick-up at the store.{% else %}Delivery to:\n{% for riga in indirizzo %}{{ riga }}\n{% endfor %}{% endif %}\n\n"
            + "You can see your order here: {{ link_ordine }}";

        Aggiungi(
            CodiciModelloEmail.OrdineConfermato, en,
            "Order no. {{ numero }} confirmed - {{ nome_sito }}",
            "<p>Thank you for your order no. {{ numero }} on {{ nome_sito }}: we have received the payment.</p>\n" + dettaglioHtmlEn,
            "Thank you for your order no. {{ numero }} on {{ nome_sito }}: we have received the payment.\n\n" + dettaglioTestoEn);
        Aggiungi(
            CodiciModelloEmail.OrdineRicevutoBonifico, en,
            "Order no. {{ numero }} received: bank transfer instructions - {{ nome_sito }}",
            "<p>Thank you for your order no. {{ numero }} on {{ nome_sito }}. To complete it, make a bank transfer with these details:</p>\n"
            + "<p>Amount: {{ importo }}<br>Account holder: {{ intestatario }}<br>IBAN: {{ iban }}<br>{% if bic %}BIC/SWIFT: {{ bic }}<br>{% endif %}{% if banca %}Bank: {{ banca }}<br>{% endif %}Reference: {{ causale }}</p>\n"
            + "{% if scadenza %}<p>Make the transfer by {{ scadenza }}: after that date the order is cancelled.</p>\n{% endif %}"
            + "{% if istruzioni %}<p>{{ istruzioni | escape | newline_to_br | raw }}</p>\n{% endif %}"
            + "<p>As soon as we receive the payment we will write to confirm the order.</p>\n" + dettaglioHtmlEn,
            "Thank you for your order no. {{ numero }} on {{ nome_sito }}. To complete it, make a bank transfer with these details:\n\n"
            + "Amount: {{ importo }}\nAccount holder: {{ intestatario }}\nIBAN: {{ iban }}\n"
            + "{% if bic %}BIC/SWIFT: {{ bic }}\n{% endif %}{% if banca %}Bank: {{ banca }}\n{% endif %}Reference: {{ causale }}\n"
            + "{% if scadenza %}Make the transfer by {{ scadenza }}: after that date the order is cancelled.\n{% endif %}"
            + "{% if istruzioni %}\n{{ istruzioni }}\n{% endif %}\n"
            + "As soon as we receive the payment we will write to confirm the order.\n\n" + dettaglioTestoEn);
        Aggiungi(
            CodiciModelloEmail.OrdineRicevutoContrassegno, en,
            "Order no. {{ numero }} received - {{ nome_sito }}",
            "<p>Thank you for your order no. {{ numero }} on {{ nome_sito }}: we have received it and we are preparing it for shipping.</p>\n"
            + "<p>You will pay {{ importo }} on delivery (cash on delivery).</p>\n"
            + "{% if istruzioni %}<p>{{ istruzioni | escape | newline_to_br | raw }}</p>\n{% endif %}" + dettaglioHtmlEn,
            "Thank you for your order no. {{ numero }} on {{ nome_sito }}: we have received it and we are preparing it for shipping.\n"
            + "You will pay {{ importo }} on delivery (cash on delivery).\n"
            + "{% if istruzioni %}{{ istruzioni }}\n{% endif %}\n" + dettaglioTestoEn);
        Aggiungi(
            CodiciModelloEmail.OrdineNuovoAmministratore, en,
            "New order no. {{ numero }} - {{ nome_sito }}",
            "<p>{% if tipo == 'bonifico' %}New order no. {{ numero }} on {{ nome_sito }} awaiting a bank transfer: {{ importo }}, {{ articoli }} items."
            + "{% elsif tipo == 'contrassegno' %}New cash on delivery order no. {{ numero }} on {{ nome_sito }}, to be shipped: {{ importo }}, {{ articoli }} items."
            + "{% else %}New paid order no. {{ numero }} on {{ nome_sito }}: {{ importo }}, {{ articoli }} items.{% endif %}</p>\n"
            + "<p>Customer: {{ cliente_nome }} ({{ cliente_email }})<br>Delivery: {{ consegna_nome }}</p>\n"
            + "<p>Manage the order here: <a href=\"{{ link_ordine }}\">{{ link_ordine }}</a></p>",
            "{% if tipo == 'bonifico' %}New order no. {{ numero }} on {{ nome_sito }} awaiting a bank transfer: {{ importo }}, {{ articoli }} items."
            + "{% elsif tipo == 'contrassegno' %}New cash on delivery order no. {{ numero }} on {{ nome_sito }}, to be shipped: {{ importo }}, {{ articoli }} items."
            + "{% else %}New paid order no. {{ numero }} on {{ nome_sito }}: {{ importo }}, {{ articoli }} items.{% endif %}\n"
            + "Customer: {{ cliente_nome }} ({{ cliente_email }})\nDelivery: {{ consegna_nome }}\nManage the order here: {{ link_ordine }}");
        Aggiungi(
            CodiciModelloEmail.Rimborso, en,
            "Refund for order no. {{ numero }} - {{ nome_sito }}",
            "<p>{% if totale %}We have fully refunded order no. {{ numero }} on {{ nome_sito }}: {{ importo }}.{% else %}We have refunded part of order no. {{ numero }} on {{ nome_sito }}: {{ importo }}.{% endif %}</p>\n"
            + "{% if motivo %}<p>Reason: {{ motivo }}</p>\n{% endif %}"
            + "<p>The credit to the payment method you used may take a few days.</p>\n"
            + "<p>You can see your order here: <a href=\"{{ link_ordine }}\">{{ link_ordine }}</a></p>",
            "{% if totale %}We have fully refunded order no. {{ numero }} on {{ nome_sito }}: {{ importo }}.{% else %}We have refunded part of order no. {{ numero }} on {{ nome_sito }}: {{ importo }}.{% endif %}\n"
            + "{% if motivo %}Reason: {{ motivo }}\n{% endif %}"
            + "The credit to the payment method you used may take a few days.\nYou can see your order here: {{ link_ordine }}");
        Aggiungi(
            CodiciModelloEmail.Prova, en,
            "Test email",
            "<p>This is a test email sent from the tenant's configuration. If you receive it, sending works.</p>",
            "This is a test email sent from the tenant's configuration. If you receive it, sending works.");

        return contenuti;
    }
}
