import { useState, type Dispatch, type SetStateAction } from 'react';
import { proposeEvolutionMutation } from '../services/evolutionOperationsApi';
import { projectService } from '../services/projectService';
import { useHelperHubContext } from './useHelperHubContext';
import type { VirtualFile } from '../types';

export type BuilderVisibleMutation = {
  filePath: string;
  oldCode: string;
  newCode: string;
  id: string;
};

type UseBuilderMutationFlowArgs = {
  selectedFile: VirtualFile | null;
  setBuildLogs: Dispatch<SetStateAction<string[]>>;
  setEditorContent: Dispatch<SetStateAction<string>>;
  setIsDirty: Dispatch<SetStateAction<boolean>>;
  onMutationActivity?: (summary: string, detail?: string, tone?: 'neutral' | 'success' | 'warning' | 'danger', relatedPath?: string) => void;
};

export function useBuilderMutationFlow({
  selectedFile,
  setBuildLogs,
  setEditorContent,
  setIsDirty,
  onMutationActivity,
}: UseBuilderMutationFlowArgs) {
  const [previewMutation, setPreviewMutation] = useState<BuilderVisibleMutation | null>(null);
  const { activeMutation, dismissActiveMutation } = useHelperHubContext();

  const visibleMutation = activeMutation
    ? {
        id: activeMutation.id,
        filePath: activeMutation.filePath,
        oldCode: activeMutation.originalCode,
        newCode: activeMutation.proposedCode,
      }
    : previewMutation;

  const handleApproveMutation = async () => {
    if (!visibleMutation) {
      return;
    }

    try {
      await projectService.applyMutation({
        id: visibleMutation.id,
        filePath: visibleMutation.filePath,
        originalCode: visibleMutation.oldCode,
        proposedCode: visibleMutation.newCode,
      });

      const normalizedMutationPath = visibleMutation.filePath.replace(/\\/g, '/');
      if (selectedFile && (
        selectedFile.path === visibleMutation.filePath ||
        normalizedMutationPath.endsWith(`/${selectedFile.path}`)
      )) {
        setEditorContent(visibleMutation.newCode);
        setIsDirty(false);
      }

      setBuildLogs(prev => [...prev, `🧬 Mutation applied: ${visibleMutation.filePath}`]);
      onMutationActivity?.('Mutation applied', visibleMutation.filePath, 'success', visibleMutation.filePath);
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Mutation apply failed.';
      setBuildLogs(prev => [...prev, `❌ ${message}`]);
      onMutationActivity?.('Mutation apply failed', message, 'danger', visibleMutation.filePath);
    } finally {
      setPreviewMutation(null);
      dismissActiveMutation();
    }
  };

  const handleRejectMutation = () => {
    setPreviewMutation(null);
    dismissActiveMutation();
  };

  const handleProposeMutation = async () => {
    setBuildLogs(prev => [...prev, '> Requesting evolution proposal from backend health monitor...']);
    try {
      const result = await proposeEvolutionMutation();
      if (result.mutation) {
        setPreviewMutation({
          id: result.mutation.id,
          filePath: result.mutation.filePath,
          oldCode: result.mutation.originalCode,
          newCode: result.mutation.proposedCode,
        });
        setBuildLogs(prev => [...prev, `🧠 Mutation proposed for ${result.mutation.filePath}`]);
        onMutationActivity?.('Mutation proposed', result.mutation.filePath, 'warning', result.mutation.filePath);
        return;
      }

      setBuildLogs(prev => [...prev, 'ℹ️ Backend health is currently stable. No mutation proposal returned.']);
      onMutationActivity?.('Mutation skipped', 'Backend returned no proposal.', 'neutral');
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Failed to request mutation proposal.';
      setBuildLogs(prev => [...prev, `❌ ${message}`]);
      onMutationActivity?.('Mutation request failed', message, 'danger');
    }
  };

  const clearPreviewMutation = () => setPreviewMutation(null);

  return {
    visibleMutation,
    clearPreviewMutation,
    handleApproveMutation,
    handleRejectMutation,
    handleProposeMutation,
  };
}
