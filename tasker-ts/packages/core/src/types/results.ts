/** Four-variant discriminated union matching C# TaskResult */
export type TaskResult =
  | { readonly type: 'success'; readonly message: string }
  | { readonly type: 'not-found'; readonly taskId: string }
  | { readonly type: 'no-change'; readonly message: string }
  | { readonly type: 'error'; readonly message: string };

export type DataResult<T> =
  | { readonly type: 'success'; readonly data: T; readonly message: string }
  | { readonly type: 'not-found'; readonly taskId: string }
  | { readonly type: 'no-change'; readonly message: string }
  | { readonly type: 'error'; readonly message: string };

export interface BatchResult {
  readonly results: readonly TaskResult[];
}

// Helper functions
export function isSuccess(r: TaskResult): r is { type: 'success'; message: string } {
  return r.type === 'success';
}

export function isError(r: TaskResult): boolean {
  return r.type === 'error' || r.type === 'not-found';
}

export function successCount(batch: BatchResult): number {
  return batch.results.filter(r => r.type === 'success').length;
}

export function anyFailed(batch: BatchResult): boolean {
  return batch.results.some(r => isError(r));
}
