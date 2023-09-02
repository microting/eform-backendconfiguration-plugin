export interface AdHocTaskWorkers {
  taskWorkers: AdHocTaskWorker[];
}

export interface AdHocTaskWorker {
  workerId: number;
  workerName: string;
  statValue: number;
}
