export const log: typeof console.log = (...args) =>
  console.log('[TASKS]:', ...args);
