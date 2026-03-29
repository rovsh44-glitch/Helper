import { helperApi } from './generatedApiClient';

export async function getTemplateCatalog() {
  return helperApi.getTemplates();
}

export async function planArchitectureDraft(body: Parameters<typeof helperApi.planArchitecture>[0]) {
  return helperApi.planArchitecture(body);
}

export async function planStrategyDraft(body: Parameters<typeof helperApi.planStrategy>[0]) {
  return helperApi.planStrategy(body);
}
