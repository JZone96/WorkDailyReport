# 📘 WorkReport

## 🎯 Obiettivo
WorkReport è un tool **console in .NET 8** pensato per automatizzare la raccolta, l’analisi e il riassunto delle attività lavorative giornaliere.
Lo scopo è produrre un **report quotidiano** (in formato Markdown/CSV) che documenti in modo affidabile come sono state spese le ore lavorative, integrando:
- applicazioni e finestre usate
- siti web visitati
- tempi di inattività (AFK)
- commit effettuati nei repository Git locali

---

## 🛠 Tool necessari

- **.NET 8 SDK**
- **ActivityWatch**:
  - `aw-server`
  - `aw-watcher-window`
  - `aw-watcher-afk`
  - *(Opzionale)* `aw-watcher-web` (Chrome/Firefox)
- **SQLite**
- **Git CLI** nel PATH *(oppure `LibGit2Sharp` via NuGet)*
- **Serilog** (logging su file)
- *(Opzionale)* Editor Visual Studio o VS Code (per riconoscimento sessioni coding)

### Orari di lavoro configurabili
Nel file `config/appsettings.json` la sezione `WorkReport:WorkHours` permette di dichiarare l'orario standard (`Start`/`End`) e la pausa pranzo. Con `DailyOverrides` puoi specificare eccezioni per singolo giorno (ad esempio pausa 13:30-14:30 lun/ven e 12:30-13:30 mar-gio). Il runner usa queste finestre per includere solo gli eventi all'interno dell'orario lavorativo.

### Linee guida per la UI futura
- Mostrare timeline giornaliera con focus block evidenziati e i promemoria (5 minuti prima di inizio/fine lavoro e pausa).
- Evidenziare i reminder come notifiche (prossimo evento/pausa) agganciati al tempo reale.
- Consentire i toggle per filtrare fonti (ActivityWatch, Git, Calendario, Scheduler).
- Panoramica progetti (derivata dai blocchi IDE) con conteggio eventi e commit associati.
- Sezioni dedicate ad AFK/meeting per spiegare i buchi temporali.

---

## 🏗 Architettura

1. **Configurazione** – `appsettings.json` con orari, filtri, mapping, privacy
2. **Raccolta dati** – ActivityWatch API (`window`, `web`, `afk`)
3. **ETL** – normalizzazione, filtri, merge eventi, commit Git
4. **Persistenza** – SQLite/DuckDB (`events_raw`, `events_clean`, `daily_rollup`)
5. **Report** – generazione Markdown/CSV
6. **Notifiche** – invio via Email/Slack/Notion

---

## 📂 Roadmap dettagliata

### A. Setup iniziale
- [x] Creare progetto **Console .NET 8**
- [x] Aggiungere pacchetti (Config, Logging, Storage, HttpClient)
- [x] Strutturare cartelle (`config/`, `data/`, `reports/`, `logs/`)
- [x] Definire `appsettings.json` (orari, endpoint, soglie, mapping, privacy, output)

### B. Raccolta dati
- [x] Interrogare **ActivityWatch API REST** (`window`, `web`, `afk`)
- [x] Definire intervallo giornaliero (09:00–18:00 Europe/Rome)
- [ ] Integrare fonti opzionali:
  - Git commit del giorno
    - [x] Localizzare repo `.git` (pruning o `git rev-parse`)
    - [c] Estrarre commit (`git log` o `LibGit2Sharp`)
    - [c] Associare commit a blocchi coding (±15m)
  - WakaTime
  - [c] Calendario
  - Jira/Asana/Trello

### C. Normalizzazione & Pulizia
- [c] Schema uniforme (`ts_start`, `ts_end`, `duration`, `app`, `title`, `url`)
- [ ] Regole:
  - Orario lavoro + pausa esclusa 
  - Gestione notifica eventi: Pausa tra 5 minuti, inizio e fine
  - Scartare eventi < soglia (es. 10s)
  - Merge contigui (gap ≤ 60s)
  - Separare AFK
- [ ] Filtri privacy: blacklist + anonimizzazione URL

### D. Mapping Progetti/Categorie
- [ ] Definire regole JSON/YAML (ordine conta)
- [ ] Esempi:
  - `VS Code + repo fit-*` → FITP
  - `teams|zoom|meet` → Meeting
  - `gitlab|github .* /fit-management` → Fit Management
- [ ] Default: “non categorizzato”

### E. Persistenza locale
- [ ] Salvare in DB:
  - `events_raw`
  - `events_clean`
  - `daily_rollup`
- [ ] Vantaggi: storico, debug, report settimanali

### F. Calcolo indicatori
- [ ] Tempo totale giornaliero
- [ ] Distribuzione categorie (deep work, meeting, comms, web)
- [ ] Top progetti/app
- [ ] Focus block ≥ 25 min
- [ ] (Opz.) commit, PR, ticket

### G. Generazione Report
- [ ] File Markdown: `reports/daily-YYYY-MM-DD.md`
- [ ] Struttura:
  - Header con data + tempo totale
  - Tabella attività
  - Sintesi manager-friendly (6–8 righe)
  - Prossimi passi (3 bullet)
- [ ] (Opz.) CSV
- [ ] (Opz.) Sintesi con LLM

### H. Consegna/Notifiche
- [ ] Email (MailKit)
- [ ] Slack (webhook)
- [ ] Notion (API)
- [ ] Gestione errori: retry + fallback locale

### I. Pianificazione
- [ ] **Windows Task Scheduler**:
  - Trigger: 18:05, lun–ven
  - Azione: `WorkReport.exe -date=today`
  - Run anche a schermo bloccato
- [ ] (Opz.) Worker Service + cron

### J. Test & Taratura
- [ ] Lanciare per date passate (`-date=YYYY-MM-DD`)
- [ ] Confrontare con durata percepita
- [ ] Raffinare regex, soglie, blacklist
- [ ] Validare pausa/weekend/privacy

### K. Manutenzione
- [ ] Rotazione log (7–14 giorni)
- [ ] Backup `reports/`
- [ ] Aggiornare ActivityWatch ogni 3 mesi
- [ ] Rivedere mapping ogni 1–2 mesi
- [ ] Audit privacy trimestrale

---

## 🔎 Diagrammi

### Architettura generale
```mermaid
flowchart LR
    subgraph User[Utente]
        VS[Visual Studio / VS Code]
        Browser[(Browser)]
    end

    subgraph AW[ActivityWatch]
        AWS[aw-server]
        AWWIN[aw-watcher-window]
        AWWEB[aw-watcher-web]
        AWAFK[aw-watcher-afk]
    end

    subgraph App[WorkReport (.NET 8)]
        CFG[Config (appsettings.json)]
        AWClient[ActivityWatchClient]
        ETL[ETL: filter/merge/mapping]
        Git[GitCommitService]
        DB[(SQLite / DuckDB)]
        RPT[Report Builder (Markdown/CSV)]
        NOTIF[Notifier (Email/Slack/Notion)]
    end

    VS --> AWWIN
    Browser --> AWWEB
    User --> AWAFK

    AWWIN --> AWS
    AWWEB --> AWS
    AWAFK --> AWS

    CFG --> AWClient
    CFG --> ETL
    CFG --> Git
    CFG --> RPT
    CFG --> NOTIF
    AWClient -->|/buckets /events| ETL
    Git --> ETL
    ETL --> DB
    DB --> RPT
    RPT --> NOTIF
