import { BuilderLaunchRequest, DeploymentPlatform, GeneratedProject, MutationProposal } from '../types';
import { helperGenerationService } from './helperGenerationService';
import { helperApi } from './generatedApiClient';
import { buildVirtualFolder, getParentRelativePath, normalizeRelativePath, toVirtualFolder } from './workspaceTree';

export class ProjectService {
  private currentProject: GeneratedProject | null = null;
  private contentCache: Record<string, string> = {};

  public async createProject(
    prompt: string,
    platform: DeploymentPlatform,
    launchContext: BuilderLaunchRequest | null = null,
  ): Promise<GeneratedProject> {
    const result = await helperGenerationService.generate(prompt);

    if (!result.success) {
        throw new Error(result.errors.map(e => e.message).join(', ') || "Generation failed");
    }

    const name = result.projectPath.split(/[/\\]/).pop() || "Generated";
    
    this.currentProject = {
      id: crypto.randomUUID(),
      name: name,
      fullPath: result.projectPath,
      targetPlatform: platform,
      launchContext,
      root: buildVirtualFolder(name, result.files.map(f => f.relativePath)),
      status: 'compiled'
    };

    // Cache using relative paths as keys
    for (const file of result.files) {
        this.contentCache[file.relativePath] = file.content;
    }
    
    return this.currentProject;
  }

  public async getFileContent(relativePath: string): Promise<string> {
      const normalizedPath = normalizeRelativePath(relativePath);
      if (this.contentCache[normalizedPath] !== undefined) {
        return this.contentCache[normalizedPath];
      }

      if (!this.currentProject) {
        return 'Content not found in cache';
      }

      const result = await helperApi.readWorkspaceFile({
        projectPath: this.currentProject.fullPath,
        relativePath: normalizedPath,
      });
      this.contentCache[normalizedPath] = result.content;
      return result.content;
  }

  public async updateFileContent(relativePath: string, newContent: string): Promise<void> {
    if (!this.currentProject) return;
    
    // Path for backend must be relative to root or absolute if guard allows.
    // We send the full path because FileSystemGuard will verify it.
    const fullPath = `${this.currentProject.fullPath}/${relativePath}`.replace(/\\/g, '/');

    const result = await helperApi.writeFile({ path: fullPath, content: newContent });
    if (!result.success) throw new Error("Security Guard blocked file write.");

    this.contentCache[normalizeRelativePath(relativePath)] = newContent;
  }

  public async applyMutation(mutation: MutationProposal): Promise<void> {
    const targetPath = mutation.filePath;
    if (this.isAbsolutePath(targetPath)) {
      const result = await helperApi.writeFile({ path: targetPath, content: mutation.proposedCode });
      if (!result.success) throw new Error('Security Guard blocked mutation apply.');
    } else {
      const result = await helperApi.writeRelativeFile({ path: targetPath, content: mutation.proposedCode });
      if (!result.success) throw new Error('Security Guard blocked mutation apply.');
    }

    const projectRelativePath = this.tryResolveProjectRelativePath(targetPath);
    if (projectRelativePath) {
      this.contentCache[projectRelativePath] = mutation.proposedCode;
    }
  }

  public async runBuild(): Promise<any> {
    if (!this.currentProject) return { success: false, errors: [] };
    
    return await helperApi.build({ projectPath: this.currentProject.fullPath });
  }

  public getCurrentProject(): GeneratedProject | null {
    return this.currentProject;
  }

  public clearCurrentProject(): void {
    this.currentProject = null;
    this.contentCache = {};
  }

  public async openProject(projectPath: string, platform: DeploymentPlatform = DeploymentPlatform.CLI): Promise<GeneratedProject> {
    const result = await helperApi.openWorkspace({ projectPath });
    this.currentProject = {
      id: crypto.randomUUID(),
      name: result.project.name,
      fullPath: result.project.fullPath,
      targetPlatform: platform,
      launchContext: null,
      root: toVirtualFolder(result.project.root),
      status: 'compiled',
    };
    this.contentCache = {};
    return this.currentProject;
  }

  public async refreshProject(): Promise<GeneratedProject | null> {
    if (!this.currentProject) {
      return null;
    }

    const existingPath = this.currentProject.fullPath;
    const existingPlatform = this.currentProject.targetPlatform;
    return this.openProject(existingPath, existingPlatform);
  }

  public async createWorkspaceNode(parentRelativePath: string, name: string, isFolder: boolean): Promise<void> {
    if (!this.currentProject) {
      throw new Error('No active workspace.');
    }

    await helperApi.createWorkspaceNode({
      projectPath: this.currentProject.fullPath,
      parentRelativePath: normalizeRelativePath(parentRelativePath),
      name,
      isFolder,
    });

    await this.refreshProject();
  }

  public async renameWorkspaceNode(relativePath: string, newName: string): Promise<void> {
    if (!this.currentProject) {
      throw new Error('No active workspace.');
    }

    const normalizedPath = normalizeRelativePath(relativePath);
    await helperApi.renameWorkspaceNode({
      projectPath: this.currentProject.fullPath,
      relativePath: normalizedPath,
      newName,
    });

    this.clearCachedPath(normalizedPath);
    await this.refreshProject();
  }

  public async deleteWorkspaceNode(relativePath: string): Promise<void> {
    if (!this.currentProject) {
      throw new Error('No active workspace.');
    }

    const normalizedPath = normalizeRelativePath(relativePath);
    await helperApi.deleteWorkspaceNode({
      projectPath: this.currentProject.fullPath,
      relativePath: normalizedPath,
    });

    this.clearCachedPath(normalizedPath);
    await this.refreshProject();
  }

  private tryResolveProjectRelativePath(path: string): string | null {
    if (!this.currentProject) {
      return this.contentCache[path] !== undefined ? path : null;
    }

    if (this.contentCache[path] !== undefined) {
      return path;
    }

    const normalizedProjectRoot = this.normalizePath(this.currentProject.fullPath);
    const normalizedPath = this.normalizePath(path);
    const projectPrefix = `${normalizedProjectRoot}/`;
    if (!normalizedPath.startsWith(projectPrefix)) {
      return null;
    }

    const relativePath = normalizedPath.slice(projectPrefix.length);
    return this.contentCache[relativePath] !== undefined ? relativePath : relativePath;
  }

  private isAbsolutePath(path: string): boolean {
    return /^[a-zA-Z]:[\\/]/.test(path) || path.startsWith('\\\\') || path.startsWith('/');
  }

  private normalizePath(path: string): string {
    return path.replace(/\\/g, '/').replace(/\/+$/, '');
  }

  public getParentPath(relativePath: string): string {
    return getParentRelativePath(relativePath);
  }

  private clearCachedPath(relativePath: string): void {
    const normalizedPath = normalizeRelativePath(relativePath);
    for (const key of Object.keys(this.contentCache)) {
      if (key === normalizedPath || key.startsWith(`${normalizedPath}/`)) {
        delete this.contentCache[key];
      }
    }
  }
}

export const projectService = new ProjectService();
