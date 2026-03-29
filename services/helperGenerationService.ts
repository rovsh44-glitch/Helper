import { helperApi } from './generatedApiClient';

export interface HelperGenerationResult {
  success: boolean;
  files: {
    relativePath: string;
    content: string;
    language: string;
  }[];
  projectPath: string;
  errors: {
    file: string;
    line: number;
    code: string;
    message: string;
  }[];
  duration: string;
}

export const helperGenerationService = {
  async generate(prompt: string, type: string = 'general'): Promise<HelperGenerationResult> {
    void type;
    const response = await helperApi.generate({ prompt });
    return response as HelperGenerationResult;
  },
};
