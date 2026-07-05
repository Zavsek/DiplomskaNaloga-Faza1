import { logErrorToDatabase, db } from './db.js';
async function runMain() {
    console.log("Začenjam test delovanja in generiranje baze...");
    try {
        await logErrorToDatabase({
            modelId: "Test",
            expected: "HTTP Status 401 Unauthorized",
            received: "HTTP Status 500 Internal Server Error",
            details: "Model se je sesul ob preverjanju neveljavnega JWT žetona znotraj avtentikacijskega middleware-a."
        });
        console.log("✅ Osnovni test uspešen! Baza in tabela sta ustvarjeni ter napolnjeni.");
    }
    catch (error) {
        console.error("Napaka pri testu:", error);
    }
    finally {
        db.close((err) => {
            if (err) {
                console.error("Napaka pri zapiranju baze:", err.message);
            }
            else {
                console.log("Povezava z bazo varno zaprta.");
            }
        });
    }
}
runMain();
