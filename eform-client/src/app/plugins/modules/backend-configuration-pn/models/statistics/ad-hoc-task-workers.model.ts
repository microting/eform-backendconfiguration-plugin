export interface AdHocTaskWorkers {
  taskWorkers: AdHocTaskWorker[];
}

export interface AdHocTaskWorker {
  workerName: string;
  statValue: number;
}
