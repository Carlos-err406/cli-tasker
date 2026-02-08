type TrySuccess<T> = [null, T];
type TryError = { message: string; stack?: string };
type TryFailure = [TryError, null];
export type TryResult<T> = Promise<TrySuccess<T> | TryFailure>;

function normalizeError(error: unknown): TryError {
  if (error instanceof Error) {
    return { message: error.message, stack: error.stack };
  }
  return { message: String(error) };
}

export default async function $try<T>(fn: () => Promise<T> | T): TryResult<T> {
  try {
    const result = await fn();
    return [null, result];
  } catch (error) {
    return [normalizeError(error), null];
  }
}
