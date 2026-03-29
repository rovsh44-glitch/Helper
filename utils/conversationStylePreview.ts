export interface ConversationStylePreviewConfig {
  responseStyle: string;
  preferredLanguage: string;
  warmth: string;
  enthusiasm: string;
  directness: string;
  defaultAnswerShape: string;
}

export interface ConversationStylePreviewModel {
  summary: string[];
  preview: string;
}

export function buildConversationStylePreview(config: ConversationStylePreviewConfig): ConversationStylePreviewModel {
  const isRussian = config.preferredLanguage === 'ru';
  const summary = [
    describeWarmth(config.warmth, isRussian),
    describeEnthusiasm(config.enthusiasm, isRussian),
    describeDirectness(config.directness, isRussian),
    describeAnswerShape(config.defaultAnswerShape, isRussian),
    describeDetail(config.responseStyle, isRussian),
  ];

  return {
    summary,
    preview: buildPreviewText(config, isRussian),
  };
}

function buildPreviewText(config: ConversationStylePreviewConfig, isRussian: boolean): string {
  const lead = isRussian
    ? config.directness === 'direct'
      ? 'Короткий вывод: сейчас логичнее идти небольшими шагами.'
      : config.warmth === 'warm'
        ? 'Сейчас самый практичный путь выглядит так: можно двигаться небольшими и понятными шагами.'
        : 'Самый практичный путь сейчас: двигаться небольшими шагами.'
    : config.directness === 'direct'
      ? 'Bottom line: the most practical move is to proceed in small steps.'
      : config.warmth === 'warm'
        ? 'The most practical path here is to move in small, clear steps.'
        : 'The practical path here is to move in small steps.';

  const support = isRussian
    ? config.responseStyle === 'concise'
      ? 'Так ниже риск лишней сложности и проще быстро скорректировать направление.'
      : config.responseStyle === 'detailed'
        ? 'Так проще удержать качество, вовремя заметить ограничения и без лишней переделки уточнить следующий шаг.'
        : 'Так проще удержать качество и при необходимости быстро уточнить следующий шаг.'
    : config.responseStyle === 'concise'
      ? 'That keeps the risk of unnecessary complexity low and makes course correction easy.'
      : config.responseStyle === 'detailed'
        ? 'That makes it easier to preserve quality, surface constraints early, and refine the next move without rework.'
        : 'That keeps quality stable while leaving room to refine the next move quickly.';

  const closing = isRussian
    ? config.enthusiasm === 'high'
      ? 'Если хотите, дальше сразу разложу это в исполнимый мини-план.'
      : 'Если нужно, могу сразу разложить это в следующий практический шаг.'
    : config.enthusiasm === 'high'
      ? 'If helpful, I can turn that into an executable mini-plan next.'
      : 'If useful, I can turn that into the next practical step.';

  if (config.defaultAnswerShape === 'bullets') {
    return isRussian
      ? `- ${trimPeriod(lead)}\n- ${trimPeriod(support)}\n- ${trimPeriod(closing)}`
      : `- ${trimPeriod(lead)}\n- ${trimPeriod(support)}\n- ${trimPeriod(closing)}`;
  }

  return `${lead} ${support} ${closing}`.trim();
}

function describeWarmth(value: string, isRussian: boolean): string {
  return isRussian
    ? value === 'warm'
      ? 'Теплота: заметно теплее обычного'
      : value === 'cool'
        ? 'Теплота: сдержанно и без лишней мягкости'
        : 'Теплота: нейтральный баланс'
    : value === 'warm'
      ? 'Warmth: noticeably warmer than default'
      : value === 'cool'
        ? 'Warmth: restrained and more neutral'
        : 'Warmth: balanced';
}

function describeEnthusiasm(value: string, isRussian: boolean): string {
  return isRussian
    ? value === 'high'
      ? 'Энергия: выше обычной'
      : value === 'low'
        ? 'Энергия: спокойная и низкодраматичная'
        : 'Энергия: ровная'
    : value === 'high'
      ? 'Energy: higher'
      : value === 'low'
        ? 'Energy: calm and understated'
        : 'Energy: balanced';
}

function describeDirectness(value: string, isRussian: boolean): string {
  return isRussian
    ? value === 'direct'
      ? 'Прямота: ближе к делу, меньше подводок'
      : value === 'soft'
        ? 'Прямота: мягче и осторожнее'
        : 'Прямота: сбалансирована'
    : value === 'direct'
      ? 'Directness: more to the point'
      : value === 'soft'
        ? 'Directness: softer transitions'
        : 'Directness: balanced';
}

function describeAnswerShape(value: string, isRussian: boolean): string {
  return isRussian
    ? value === 'paragraph'
      ? 'Форма: по умолчанию абзацами'
      : value === 'bullets'
        ? 'Форма: по умолчанию списком'
        : 'Форма: авто'
    : value === 'paragraph'
      ? 'Shape: paragraph by default'
      : value === 'bullets'
        ? 'Shape: bullets by default'
        : 'Shape: auto';
}

function describeDetail(value: string, isRussian: boolean): string {
  return isRussian
    ? value === 'concise'
      ? 'Детальность: короче'
      : value === 'detailed'
        ? 'Детальность: глубже'
        : 'Детальность: сбалансирована'
    : value === 'concise'
      ? 'Detail: concise'
      : value === 'detailed'
        ? 'Detail: deeper'
        : 'Detail: balanced';
}

function trimPeriod(value: string): string {
  return value.trim().replace(/[.!?]+$/u, '');
}
