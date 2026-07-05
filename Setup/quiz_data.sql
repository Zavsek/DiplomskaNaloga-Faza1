INSERT INTO quizzes (id, title, duration) 
VALUES 
(1, 'Splošni Pop Kviz', '00:05:00'),
(2, 'Napredni Geografski Izziv', '00:10:00'),
(3, 'Filmska in Pop Kultura', '00:07:00'),
(4, 'Zgodovina in Mitologija', '00:08:00')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, duration = EXCLUDED.duration;

INSERT INTO questions (id, "questionText", answer, "quizId", "orderIndex") 
VALUES 
-- --- KVIZ 1: Splošni Pop Kviz (quizId = 1) ---
(1, 'Kateri planet je znan kot Rdeči planet?', 'B', 1, 1),
(2, 'Koliko celin je na Zemlji?', 'C', 1, 2),
(3, 'Katera žival je največji sesalec na svetu?', 'A', 1, 3),

-- --- KVIZ 2: Napredni Geografski Izziv (quizId = 2) ---
(4, 'Katero je glavno mesto Avstralije?', 'D', 2, 1),
(5, 'Katera reka je najdaljša na svetu?', 'A', 2, 2),
(6, 'V kateri državi leži gora Kilimandžaro?', 'B', 2, 3),
(7, 'Kateri ocean je največji?', 'A', 2, 4),
(8, 'Katera država ima največ prebivalcev na svetu?', 'C', 2, 5),

-- --- KVIZ 3: Filmska in Pop Kultura (quizId = 3) ---
(9, 'Kdo je režiral film Prvinski strah (Inception)?', 'B', 3, 1),
(10, 'Iz katere države prihaja skupina ABBA?', 'A', 3, 2),
(11, 'Kateri igralec igra lika Iron Man v Marvel filmih?', 'C', 3, 3),
(12, 'Koliko oskarjev je prejel film Titanik?', 'D', 3, 4),
(13, 'Katera serija vsebuje izmišljeno celino Westeros?', 'A', 3, 5),

-- --- KVIZ 4: Zgodovina in Mitologija (quizId = 4) ---
(14, 'Katero leto je padel Berlinski zid?', 'C', 4, 1),
(15, 'Kdo je bil vrhovni bog v grški mitologiji?', 'A', 4, 2),
(16, 'Katera civilizacija je zgradila Machu Picchu?', 'B', 4, 3),
(17, 'Kdo je bil prvi predsednik Združenih držav Amerike?', 'D', 4, 4),
(18, 'V katerem stoletju se je začela industrijska revolucija?', 'B', 4, 5)

ON CONFLICT (id) DO UPDATE SET 
    "questionText" = EXCLUDED."questionText", 
    answer = EXCLUDED.answer, 
    "quizId" = EXCLUDED."quizId", 
    "orderIndex" = EXCLUDED."orderIndex";