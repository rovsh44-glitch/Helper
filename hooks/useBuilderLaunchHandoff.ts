import { useEffect, useState, type Dispatch, type SetStateAction } from 'react';
import type { BuilderLaunchRequest, DeploymentPlatform, GeneratedProject } from '../types';

type UseBuilderLaunchHandoffArgs = {
  launchRequest?: BuilderLaunchRequest | null;
  onLaunchConsumed?: () => void;
  project: GeneratedProject | null;
  setCreatePrompt: Dispatch<SetStateAction<string>>;
  setBuildLogs: Dispatch<SetStateAction<string[]>>;
  createProjectFromPrompt: (
    prompt: string,
    platform: DeploymentPlatform,
    request?: BuilderLaunchRequest,
  ) => Promise<void>;
};

export function useBuilderLaunchHandoff({
  launchRequest = null,
  onLaunchConsumed,
  project,
  setCreatePrompt,
  setBuildLogs,
  createProjectFromPrompt,
}: UseBuilderLaunchHandoffArgs) {
  const [lastConsumedLaunchId, setLastConsumedLaunchId] = useState<string | null>(null);

  useEffect(() => {
    if (!launchRequest || launchRequest.id === lastConsumedLaunchId) {
      return;
    }

    setLastConsumedLaunchId(launchRequest.id);
    setCreatePrompt(launchRequest.prompt);

    if (project) {
      setBuildLogs(prev => [
        ...prev,
        `> Planner handoff received for template ${launchRequest.routeTemplateId || 'unrouted request'}.`,
        '> Current workspace is still open. Start a new project to apply the handoff.',
      ]);
      onLaunchConsumed?.();
      return;
    }

    setBuildLogs([
      `> Planner handoff received from ${launchRequest.source}.`,
      `> Route hint: ${launchRequest.routeTemplateId || 'none'}`,
      `> Blueprint: ${launchRequest.blueprintName || 'not specified'}`,
    ]);
    void createProjectFromPrompt(launchRequest.prompt, launchRequest.targetPlatform, launchRequest);
  }, [createProjectFromPrompt, lastConsumedLaunchId, launchRequest, onLaunchConsumed, project, setBuildLogs, setCreatePrompt]);
}
