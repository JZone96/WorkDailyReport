📘 WorkReport
🎯 Obiettivo

WorkReport è un tool console in .NET 8 pensato per automatizzare la raccolta, l’analisi e il riassunto delle attività lavorative giornaliere.
Lo scopo è produrre un report quotidiano (in formato Markdown/CSV) che documenti in modo affidabile come sono state spese le ore lavorative, integrando:

applicazioni e finestre usate,

siti web visitati,

tempi di inattività (AFK),

commit effettuati nei repository Git locali.

🛠 Tool necessari
1. Ambiente di sviluppo

.NET 8 SDK

Editor a scelta: Visual Studio / VS Code

2. Activity Tracking

ActivityWatch:

aw-server

aw-watcher-window

aw-watcher-afk

(Opzionale) aw-watcher-web (Chrome/Firefox) → se assente, gestire fallback ed evitare crash

3. Database locale

SQLite (default) o DuckDB

4. Git

Git CLI nel PATH

(Opzionale) LibGit2Sharp (NuGet)

5. Logging

Serilog (sink file)

🏗 Architettura

Configurazione: appsettings.json con orari, filtri, mapping, privacy.

Raccolta dati: ActivityWatch API (window, web, afk).

ETL: normalizzazione, filtri, merge eventi, arricchimento commit Git.

Persistenza: SQLite/DuckDB → events_raw, events_clean, daily_rollup.

Report: generazione file Markdown/CSV con sintesi giornaliera.

Notifiche: invio opzionale via email/Slack/Notion.

📂 Roadmap dettagliata
📂 A. Setup iniziale

 Creare progetto Console .NET 8

 Aggiungere pacchetti (Config, Logging, Storage, HttpClient)

 Strutturare cartelle (config/, data/, reports/, logs/)

 Definire appsettings.json con orari, endpoint, soglie, mapping, privacy, output

📡 B. Raccolta dati

 Interrogare ActivityWatch API REST (window, web, afk)

 Definire intervallo giornaliero (09:00–18:00, Europe/Rome)

 Opzionale: integrare altre fonti

Git (commit del giorno)

 Localizzare repo:

Scansione root → .git (pruning)

git rev-parse --show-toplevel

 GitCommitService per estrarre commit (git log o LibGit2Sharp)

 Associare commit ai blocchi isCodeSession (±15m)

WakaTime

Calendario

Jira/Asana/Trello

🧹 C. Normalizzazione & Pulizia

 Schema uniforme (ts_start, ts_end, duration, app, title, url)

 Regole:

Orario lavoro + pausa esclusa

Scartare < soglia (es. 10s)

Merge contigui (gap ≤ 60s)

Separare AFK

 Filtri privacy: blacklist + anonimizzazione URL

🏷 D. Mapping Progetti/Categorie

 Definire regole JSON/YAML (ordine conta)

 Esempi:

VS Code + repo fit-* → FITP

teams|zoom|meet → Meeting

gitlab|github .* /fit-management → Fit Management

 Default: “non categorizzato”

💾 E. Persistenza locale

 Salvare in DB:

events_raw

events_clean

daily_rollup

 Vantaggi: storico, debug, report settimanali

📊 F. Calcolo indicatori

 Tempo totale giornaliero

 Distribuzione categorie (deep work, meeting, comms, web)

 Top progetti/app

 Focus block ≥ 25 min

 (Opz.) commit, PR, ticket

📝 G. Generazione Report

 File Markdown: reports/daily-YYYY-MM-DD.md

 Struttura:

Header con data + tempo totale

Tabella attività

Sintesi manager-friendly (6–8 righe)

Prossimi passi (3 bullet)

 (Opz.) CSV

 (Opz.) Sintesi con LLM

📤 H. Consegna/Notifiche

 Email (MailKit)

 Slack (webhook)

 Notion (API)

 Gestione errori: retry + fallback locale

📅 I. Pianificazione

 Windows Task Scheduler:

Trigger: 18:05, lun–ven

Azione: WorkReport.exe -date=today

Run anche a schermo bloccato

 (Opz.) Worker Service + cron

🧪 J. Test & Taratura

 Lanciare per date passate (-date=YYYY-MM-DD)

 Confrontare con durata percepita

 Raffinare regex, soglie, blacklist

 Validare pausa/weekend/privacy

🔒 K. Manutenzione

 Rotazione log (7–14 giorni)

 Backup reports/

 Aggiornare ActivityWatch ogni 3 mesi

 Rivedere mapping ogni 1–2 mesi

 Audit privacy trimestrale

🔎 Diagrammi
Architettura generale
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

Flusso giornaliero
sequenceDiagram
    autonumber
    participant TS as Task Scheduler
    participant WR as WorkReport (.NET)
    participant AW as aw-server
    participant G as Git (local)
    participant DB as SQLite/DuckDB
    participant OUT as Report/Notifiche

    TS->>WR: Avvio alle 18:05
    WR->>AW: GET /buckets
    WR->>AW: GET /buckets/{window|web|afk}/events?start&end
    WR->>G: Commit del giorno (git log / LibGit2Sharp)
    WR->>WR: ETL (clip orari, soglie, merge, mapping, commit ±15m)
    WR->>DB: Scrive events_raw / events_clean / daily_rollup
    WR->>OUT: Genera Markdown/CSV
    WR->>OUT: Invia Email/Slack/Notion
    WR-->>TS: Exit code / Log
