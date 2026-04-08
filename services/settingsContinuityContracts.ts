export interface ContinuityBackgroundTask {
  id: string;
  kind: string;
  title: string;
  status: string;
  createdAtUtc: string;
  dueAtUtc?: string;
  projectId?: string;
  notes?: string;
}

export interface ContinuityProactiveTopic {
  id: string;
  topic: string;
  frequency: string;
  enabled: boolean;
  createdAtUtc: string;
  projectId?: string;
}
