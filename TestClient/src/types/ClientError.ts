export interface ClientError {
    modelId: string;
    expected: string;
    received: string;
    details: string;
}