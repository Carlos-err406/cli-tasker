export const TaskStatus = {
  Pending: 0,
  InProgress: 1,
  Done: 2,
} as const;

export type TaskStatus = (typeof TaskStatus)[keyof typeof TaskStatus];

/** Reverse mapping for display purposes */
export const TaskStatusName: Record<TaskStatus, string> = {
  [TaskStatus.Pending]: 'Pending',
  [TaskStatus.InProgress]: 'InProgress',
  [TaskStatus.Done]: 'Done',
};
