# Faza 1 — Kviz API (primerjava LLM poskusov)

Repozitorij vsebuje izhodiščno predlogo kviz API-ja, rešitve treh jezikovnih modelov (po tri poskuse) ter zbrane rezultate meritev in analiz. Za lokalno bazo in začetne podatke je na voljo tudi `Setup/` (Docker/PostgreSQL, `quiz_data.sql`).

## Predloga (`Template/`)

Predloga je začetni ASP.NET Core API za kviz. V `Program.cs` so že pripravljeni JWT nastavitve, CORS (`http://localhost:4444`) in povezava na PostgreSQL; ostale funkcionalnosti (avtentikacija, igra, chat, statistike …) dopolnijo modeli v poskusih.

### Backend (`Template/YPoskusX/`)

- ASP.NET Core API
- Entity Framework Core + PostgreSQL
- Entiteta `Quiz`, DTO `QuestionSendDto`
- Delna JWT/CORS konfiguracija v `Program.cs`

### Struktura predloge

```
Template/
  YPoskusX/          # ASP.NET Core API
    Controllers/
    Data/
    DTOs/
    Entities/
    Services/
  Template.slnx
```

## Testni odjemalec (`TestClient/`)

React + Vite odjemalec za preverjanje API-jev posameznih poskusov (povezava prek CORS na `http://localhost:4444`). Ni del predloge; uporablja se ločeno pri testiranju.

## Struktura poskusov

Za vsakega od treh LLM modelov obstajajo trije neodvisni poskusi. Vsak poskus je celotna rešitev (ASP.NET Core API), zgrajena na podlagi predloge.

```
Faza1/
  Template/                 # izhodiščna predloga
  TestClient/               # testni odjemalec
  Setup/                    # Docker / PostgreSQL / začetni podatki
  Codex5.3/
    Poskus1/ … Poskus3/     # GPT Codex 5.3
  Gemini3.1/
    Poskus1/ … Poskus3/     # Gemini Pro 3.1
  Sonnet4.6/
    Poskus1/ … Poskus3/     # Claude Sonnet 4.6
  Rezultati/                # meritve in analize
```

Vsak `PoskusN/` tipično vsebuje:

- `PoskusN/` — ASP.NET Core API projekt
- `PoskusN.slnx` — solution datoteka

## Rezultati (`Rezultati/`)

Mapa vsebuje povzetke meritev in sezname preverjanj. Številčne vrednosti so v navedenih datotekah; spodaj je le opis vsebine.

| Datoteka / mapa | Vsebina |
|-----------------|--------|
| [KakovostneKršitve.md](Rezultati/KakovostneKršitve.md) | Kršitve po CQ kriterijih (upravljanje z napakami, redundantni komentarji, anonimni/nepravilni tipi, redundantni klici PB, predolge funkcije, neustrezno poimenovanje, hardkodani nizi/magične vrednosti, gnezdenje, ponavljanje) — skupno in po poskusih za vse tri modele |
| [Sintaktične kršitve.md](Rezultati/Sintaktične%20kršitve.md) | Število sintaktičnih kršitev po modelu in poskusu |
| [Semantične Kršiteve.md](Rezultati/Semantične%20Kršiteve.md) | Število semantičnih kršitev po modelu in poskusu |
| [Časi.xlsx](Rezultati/Časi.xlsx) | Merjeni časi (npr. generiranje / izvedba poskusov) |
| [Seznami-Semantične Zahteve/](Rezultati/Seznami-Semantične%20Zahteve/) | Backend checklisti zahtev (Template ter Codex1–3, Gemini1–3, Sonnet1–3) za preverjanje semantične skladnosti rešitev |
