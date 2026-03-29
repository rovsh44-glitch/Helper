import { helperApi } from './generatedApiClient';

export async function getEvolutionLibrarySnapshot() {
  return helperApi.getEvolutionLibrary();
}

export async function getEvolutionStatusSnapshot() {
  return helperApi.getEvolutionStatus();
}

export async function runEvolutionAction(
  action: Parameters<typeof helperApi.evolutionAction>[0],
  body?: Parameters<typeof helperApi.evolutionAction>[1],
) {
  return helperApi.evolutionAction(action, body);
}

export async function runIndexingAction(
  action: Parameters<typeof helperApi.indexingAction>[0],
  body?: Parameters<typeof helperApi.indexingAction>[1],
) {
  return helperApi.indexingAction(action, body);
}

export async function challengeEvolutionRound(body: Parameters<typeof helperApi.challengeEvolution>[0]) {
  return helperApi.challengeEvolution(body);
}

export async function proposeEvolutionMutation() {
  return helperApi.proposeEvolutionMutation();
}
