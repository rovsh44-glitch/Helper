import { helperApi } from '../generatedApiClient';

export type {
  CapabilityCatalogSnapshotDto,
  ControlPlaneSnapshotDto,
  HumanLikeConversationDashboardSnapshotDto,
  RuntimeLogEntryDto,
  RuntimeLogsSnapshotDto,
  RuntimeLogSourceDto,
} from '../generatedApiClient';

export async function getControlPlaneSnapshot() {
  return helperApi.getControlPlane();
}

export async function getRuntimeLogsSnapshot(query?: { tail?: number; maxSources?: number }) {
  return helperApi.getRuntimeLogs(query);
}

export async function getCapabilityCatalogSnapshot() {
  return helperApi.getCapabilityCatalog();
}

export async function getHumanLikeConversationDashboard(days = 7) {
  return helperApi.getHumanLikeConversationDashboard(days);
}
