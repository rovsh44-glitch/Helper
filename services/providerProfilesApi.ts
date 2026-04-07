import { helperApi } from './generatedApiClient';

export type {
  ProviderActivationResultDto,
  ProviderProfileSummaryDto,
  ProviderProfilesSnapshotDto,
  ProviderRecommendationRequestDto,
  ProviderRecommendationResultDto,
} from './generatedApiClient';

export async function getProviderProfilesSnapshot() {
  return helperApi.getProviderProfiles();
}

export async function getActiveProviderProfile() {
  return helperApi.getActiveProviderProfile();
}

export async function activateProviderProfile(profileId: string) {
  return helperApi.activateProviderProfile({ profileId });
}

export async function recommendProviderProfile(body: Parameters<typeof helperApi.recommendProviderProfile>[0]) {
  return helperApi.recommendProviderProfile(body);
}
