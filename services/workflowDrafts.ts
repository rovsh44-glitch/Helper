import {
  DeploymentPlatform,
  type ArchitecturePlanAnalysis,
  type ArchitecturePlannerSeed,
  type BuilderLaunchRequest,
  type StrategicMapAnalysis,
} from '../types';

export function createArchitecturePlannerSeed(
  task: string,
  context: string,
  analysis: StrategicMapAnalysis,
): ArchitecturePlannerSeed {
  return {
    id: crypto.randomUUID(),
    source: 'strategy',
    targetOs: 'Windows',
    prompt: buildArchitecturePromptFromStrategy(task, context, analysis),
    strategyAnalysis: analysis,
  };
}

export function createBuilderLaunchRequest(
  sourcePrompt: string,
  analysis: ArchitecturePlanAnalysis,
): BuilderLaunchRequest {
  return {
    id: crypto.randomUUID(),
    source: 'planner',
    targetPlatform: DeploymentPlatform.CLI,
    prompt: buildBuilderPromptFromArchitecture(sourcePrompt, analysis),
    routeTemplateId: analysis.route.templateId ?? null,
    planSummary: analysis.plan.description,
    blueprintName: analysis.blueprint.name,
  };
}

function buildArchitecturePromptFromStrategy(
  task: string,
  context: string,
  analysis: StrategicMapAnalysis,
): string {
  const sections = [
    `Primary task:\n${task.trim()}`,
  ];

  if (context.trim().length > 0) {
    sections.push(`Operator context:\n${context.trim()}`);
  }

  if (analysis.route.templateId) {
    sections.push(
      `Preferred template route:\n${analysis.route.templateId} (${Math.round(analysis.route.confidence * 100)}% confidence)\nReason: ${analysis.route.reason}`,
    );
  }

  if (analysis.activeGoals.length > 0) {
    sections.push(
      `Active objectives:\n${analysis.activeGoals.map(goal => `- ${goal.title}${goal.description ? `: ${goal.description}` : ''}`).join('\n')}`,
    );
  }

  if (analysis.plan.reasoning.trim().length > 0) {
    sections.push(`Strategic reasoning:\n${analysis.plan.reasoning.trim()}`);
  }

  if (analysis.plan.clarifyingQuestions && analysis.plan.clarifyingQuestions.length > 0) {
    sections.push(
      `Outstanding clarifications:\n${analysis.plan.clarifyingQuestions.map(question => `- ${question}`).join('\n')}`,
    );
  }

  return sections.join('\n\n');
}

function buildBuilderPromptFromArchitecture(
  sourcePrompt: string,
  analysis: ArchitecturePlanAnalysis,
): string {
  const sections = [
    `Generate the project described below and keep the implementation aligned with the validated architecture plan.`,
    `Original request:\n${sourcePrompt.trim()}`,
    `Planner summary:\n${analysis.plan.description}`,
  ];

  if (analysis.route.templateId) {
    sections.push(
      `Preferred template route:\n${analysis.route.templateId} (${Math.round(analysis.route.confidence * 100)}% confidence)`,
    );
  }

  if (analysis.blueprint.architectureReasoning.trim().length > 0) {
    sections.push(`Blueprint reasoning:\n${analysis.blueprint.architectureReasoning.trim()}`);
  }

  if (analysis.plan.plannedFiles.length > 0) {
    sections.push(
      `Planned files:\n${analysis.plan.plannedFiles.map(file => `- ${file.path}: ${file.purpose}`).join('\n')}`,
    );
  }

  return sections.join('\n\n');
}
