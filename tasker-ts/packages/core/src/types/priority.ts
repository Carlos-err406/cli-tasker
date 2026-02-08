export const Priority = {
  High: 1,
  Medium: 2,
  Low: 3,
} as const;

export type Priority = (typeof Priority)[keyof typeof Priority];

export const PriorityName: Record<Priority, string> = {
  [Priority.High]: 'High',
  [Priority.Medium]: 'Medium',
  [Priority.Low]: 'Low',
};
