import sqlite3 from 'sqlite3';
import * as path from 'path';
import { fileURLToPath } from 'url';
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const dbPath = path.join(__dirname, 'test_results.db');
export const db = new sqlite3.Database(dbPath, (err) => {
    if (err) {
        console.error('❌ Napaka pri povezovanju z SQLite bazo:', err.message);
    }
    else {
        console.log('💾 Uspešno povezan z SQLite bazo (test_results.db).');
    }
});
db.serialize(() => {
    db.run(`
        CREATE TABLE IF NOT EXISTS client_errors (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            model_id TEXT NOT NULL,
            expected TEXT NOT NULL,
            received TEXT NOT NULL,
            details TEXT NOT NULL,
            timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
        )
    `, (err) => {
        if (err) {
            console.error('❌ Napaka pri ustvarjanju tabelce client_errors:', err.message);
        }
    });
});
export function logErrorToDatabase(error) {
    return new Promise((resolve, reject) => {
        const query = `
            INSERT INTO client_errors (model_id, expected, received, details)
            VALUES (?, ?, ?, ?)
        `;
        const stmt = db.prepare(query);
        stmt.run(error.modelId, error.expected, error.received, error.details, function (err) {
            if (err) {
                console.error(' Napaka pri zapisovanju v bazo:', err.message);
                reject(err);
            }
            else {
                console.log(` [LOGGED TO SQLITE] Napaka za model '${error.modelId}' uspešno shranjena.`);
                resolve();
            }
        });
        stmt.finalize();
    });
}
