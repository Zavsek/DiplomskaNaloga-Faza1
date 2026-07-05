import sqlite3 from 'sqlite3';
import { ClientError } from './types/ClientError.js';
export declare const db: sqlite3.Database;
export declare function logErrorToDatabase(error: ClientError): Promise<void>;
