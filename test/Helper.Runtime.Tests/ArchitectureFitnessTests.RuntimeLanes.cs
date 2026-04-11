namespace Helper.Runtime.Tests;

public partial class ArchitectureFitnessTests
{
    [Fact]
    public void Runtime_Test_Lanes_Have_Dedicated_Projects_And_Entry_Points()
    {
        var solution = File.ReadAllText(ResolveWorkspaceFile("Helper.sln"));
        var workflow = File.ReadAllText(ResolveWorkspaceFile(".github", "workflows", "runtime-test-lanes.yml"));
        var fastScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "run_fast_tests.ps1"));
        var integrationScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "run_integration_tests.ps1"));
        var evalScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "run_eval_harness_tests.ps1"));
        var certificationScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "run_certification_tests.ps1"));
        var certificationCompileScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "run_certification_compile_tests.ps1"));
        var certificationCompileLockCheckScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "check_certification_compile_lock_wait.ps1"));

        Assert.Contains("Helper.Runtime.Api.Tests", solution, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Browser.Tests", solution, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Integration.Tests", solution, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Eval.Tests", solution, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Certification.Tests", solution, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Certification.Compile.Tests", solution, StringComparison.Ordinal);

        Assert.Contains("dotnet restore Helper.sln", workflow, StringComparison.Ordinal);
        Assert.Contains("./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore", workflow, StringComparison.Ordinal);
        Assert.Contains("./scripts/run_integration_tests.ps1 -Configuration Debug -NoRestore", workflow, StringComparison.Ordinal);
        Assert.Contains("./scripts/run_eval_harness_tests.ps1 -Configuration Debug -NoRestore", workflow, StringComparison.Ordinal);
        Assert.Contains("./scripts/run_certification_tests.ps1 -Configuration Debug -NoRestore", workflow, StringComparison.Ordinal);
        Assert.Contains("./scripts/run_certification_compile_tests.ps1 -Configuration Debug -NoRestore", workflow, StringComparison.Ordinal);

        Assert.Contains("test\\Helper.Runtime.Tests\\Helper.Runtime.Tests.csproj", fastScript, StringComparison.Ordinal);
        Assert.Contains("test\\Helper.Runtime.Api.Tests\\Helper.Runtime.Api.Tests.csproj", fastScript, StringComparison.Ordinal);
        Assert.Contains("test\\Helper.Runtime.Browser.Tests\\Helper.Runtime.Browser.Tests.csproj", fastScript, StringComparison.Ordinal);
        Assert.Contains("test\\Helper.Runtime.Integration.Tests\\Helper.Runtime.Integration.Tests.csproj", integrationScript, StringComparison.Ordinal);
        Assert.Contains("test\\Helper.Runtime.Eval.Tests\\Helper.Runtime.Eval.Tests.csproj", evalScript, StringComparison.Ordinal);
        Assert.Contains("test\\Helper.Runtime.Certification.Tests\\Helper.Runtime.Certification.Tests.csproj", certificationScript, StringComparison.Ordinal);
        Assert.Contains("test\\Helper.Runtime.Certification.Compile.Tests\\Helper.Runtime.Certification.Compile.Tests.csproj", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("HELPER_CERTIFICATION_PROCESS_TRACE_PATH", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("System.Diagnostics.ProcessStartInfo", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("Acquire-LaneLock", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("runs\\", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("--results-directory", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("HELPER_DATA_ROOT", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("HELPER_LOGS_ROOT", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("taskkill /PID", certificationCompileScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-CimInstance Win32_Process", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("HELPER_CERTIFICATION_COMPILE_LOCK_WAIT_SEC", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("HELPER_CERTIFICATION_COMPILE_LOCK_POLL_SEC", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("Waiting for active certification compile lane to release lock", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("finally", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("helper_template_e2e_", certificationCompileScript, StringComparison.Ordinal);
        Assert.Contains("helper_template_cert_test_", certificationCompileScript, StringComparison.Ordinal);

        Assert.Contains("run_certification_compile_tests.ps1", certificationCompileLockCheckScript, StringComparison.Ordinal);
        Assert.Contains("Waiting for active certification compile lane to release lock", certificationCompileLockCheckScript, StringComparison.Ordinal);
        Assert.Contains("HELPER_CERTIFICATION_COMPILE_LOCK_WAIT_SEC", certificationCompileLockCheckScript, StringComparison.Ordinal);
        Assert.Contains("First run id", certificationCompileLockCheckScript, StringComparison.Ordinal);
        Assert.Contains("Second run id", certificationCompileLockCheckScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_Test_Lane_Uses_Explicit_Exclusion_Manifest()
    {
        var runtimeProject = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "Helper.Runtime.Tests.csproj"));
        var runtimeManifest = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "Helper.Runtime.Tests.RuntimeLane.props"));
        var apiManifest = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "Helper.Runtime.Tests.ApiLane.props"));

        Assert.Contains("<EnableDefaultCompileItems>false</EnableDefaultCompileItems>", runtimeProject, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Tests.RuntimeLane.props", runtimeProject, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Tests.ApiLane.props", runtimeProject, StringComparison.Ordinal);
        Assert.Contains("Compile Include=\"**\\*.cs\" Exclude=\"$(RuntimeLaneExcludedCompile)\"", runtimeProject, StringComparison.Ordinal);
        Assert.Contains("Compile Remove=\"@(RuntimeApiLaneCompile)\"", runtimeProject, StringComparison.Ordinal);

        Assert.Contains("ConversationE2ETests.cs", runtimeManifest, StringComparison.Ordinal);
        Assert.Contains("WebSearchSessionCoordinatorTests.cs", runtimeManifest, StringComparison.Ordinal);
        Assert.Contains("GenerationGuardrailsTests.cs", runtimeManifest, StringComparison.Ordinal);
        Assert.Contains("ApiSchemaTests.cs", apiManifest, StringComparison.Ordinal);
        Assert.Contains("TurnResponseWriterReasoningMetricsTests.cs", apiManifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_Test_Lanes_Use_Root_Build_Stability_Settings()
    {
        var rootBuildProps = File.ReadAllText(ResolveWorkspaceFile("Directory.Build.props"));
        var rootBuildRsp = File.ReadAllText(ResolveWorkspaceFile("Directory.Build.rsp"));
        var apiAssemblyInfo = File.ReadAllText(ResolveWorkspaceFile("src", "Helper.Api", "Properties", "AssemblyInfo.cs"));
        var runtimeAssemblyInfo = File.ReadAllText(ResolveWorkspaceFile("src", "Helper.Runtime", "Properties", "AssemblyInfo.cs"));
        var knowledgeAssemblyInfo = File.ReadAllText(ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Properties", "AssemblyInfo.cs"));
        var webResearchAssemblyInfo = File.ReadAllText(ResolveWorkspaceFile("src", "Helper.Runtime.WebResearch", "Properties", "AssemblyInfo.cs"));

        Assert.Contains("<BaseOutputPath Condition=\"'$(HelperMsbuildStateRoot)' != '' and '$(BaseOutputPath)' == ''\">$(HelperMsbuildStateRoot)bin\\$(HelperProjectRelativeDirectory)\\</BaseOutputPath>", rootBuildProps, StringComparison.Ordinal);
        Assert.Contains("<BuildInParallel>false</BuildInParallel>", rootBuildProps, StringComparison.Ordinal);
        Assert.Contains("/p:BuildInParallel=false", rootBuildRsp, StringComparison.Ordinal);

        Assert.Contains("Helper.Runtime.Api.Tests", apiAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Integration.Tests", apiAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Certification.Tests", apiAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Certification.Compile.Tests", apiAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Api.Tests", runtimeAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Integration.Tests", runtimeAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Certification.Tests", runtimeAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Certification.Compile.Tests", runtimeAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Browser.Tests", webResearchAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Integration.Tests", knowledgeAssemblyInfo, StringComparison.Ordinal);
    }

    [Fact]
    public void Certification_Lanes_Have_Correct_Nested_Build_Ownership()
    {
        var certificationProject = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Certification.Tests", "Helper.Runtime.Certification.Tests.csproj"));
        var certificationCompileProject = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Certification.Compile.Tests", "Helper.Runtime.Certification.Compile.Tests.csproj"));
        var operatorRunbook = File.ReadAllText(ResolveWorkspaceFile("doc", "operator", "HELPER_RUNTIME_TEST_LANES_2026-04-01.md"));

        Assert.DoesNotContain("TemplateCertificationServiceTests.cs", certificationProject, StringComparison.Ordinal);
        Assert.DoesNotContain("TemplatePromotionEndToEndAndChaosTests.cs", certificationProject, StringComparison.Ordinal);
        Assert.Contains("TemplateLifecycleAndCertificationTests.cs", certificationProject, StringComparison.Ordinal);
        Assert.Contains("TemplateRoutingAndDiagnosticsTests.cs", certificationProject, StringComparison.Ordinal);

        Assert.Contains("TemplateCertificationServiceTests.cs", certificationCompileProject, StringComparison.Ordinal);
        Assert.Contains("TemplatePromotionEndToEndAndChaosTests.cs", certificationCompileProject, StringComparison.Ordinal);
        Assert.Contains("DotnetTestCommandSupportTests.cs", certificationCompileProject, StringComparison.Ordinal);
        Assert.Contains("GenerationGuardrailsTests.cs", certificationCompileProject, StringComparison.Ordinal);

        Assert.Contains("must not own tests that construct `GenerationCompileGate(new DotnetService())`", operatorRunbook, StringComparison.Ordinal);
        Assert.Contains("LocalBuildExecutor(new DotnetService())", operatorRunbook, StringComparison.Ordinal);
    }

    [Fact]
    public void Eval_Lane_Has_Explicit_Ownership_And_Does_Not_Leak_Into_Other_Lanes()
    {
        var evalProject = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Eval.Tests", "Helper.Runtime.Eval.Tests.csproj"));
        var certificationProject = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Certification.Tests", "Helper.Runtime.Certification.Tests.csproj"));
        var apiProject = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Api.Tests", "Helper.Runtime.Api.Tests.csproj"));
        var apiManifest = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "Helper.Runtime.Tests.ApiLane.props"));
        var operatorRunbook = File.ReadAllText(ResolveWorkspaceFile("doc", "operator", "HELPER_RUNTIME_TEST_LANES_2026-04-01.md"));

        Assert.Contains("EvalTestPackageFactory.cs", evalProject, StringComparison.Ordinal);
        Assert.Contains("EvalHarnessTests.cs", evalProject, StringComparison.Ordinal);
        Assert.Contains("EvalRunnerV2Tests.cs", evalProject, StringComparison.Ordinal);
        Assert.Contains("HumanLikeCommunicationEvalTests.cs", evalProject, StringComparison.Ordinal);
        Assert.Contains("WebResearchParityEvalTests.cs", evalProject, StringComparison.Ordinal);

        Assert.DoesNotContain("EvalTestPackageFactory.cs", certificationProject, StringComparison.Ordinal);
        Assert.DoesNotContain("EvalHarnessTests.cs", certificationProject, StringComparison.Ordinal);
        Assert.DoesNotContain("EvalRunnerV2Tests.cs", certificationProject, StringComparison.Ordinal);
        Assert.DoesNotContain("HumanLikeCommunicationEvalTests.cs", certificationProject, StringComparison.Ordinal);
        Assert.DoesNotContain("WebResearchParityEvalTests.cs", certificationProject, StringComparison.Ordinal);

        Assert.DoesNotContain("EvalTestPackageFactory.cs", apiProject, StringComparison.Ordinal);
        Assert.DoesNotContain("HumanLikeCommunicationEvalTests.cs", apiManifest, StringComparison.Ordinal);

        Assert.Contains("Entry point: `scripts/run_eval_harness_tests.ps1`", operatorRunbook, StringComparison.Ordinal);
        Assert.Contains("EvalOffline", operatorRunbook, StringComparison.Ordinal);
        Assert.Contains("EvalV2", operatorRunbook, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_Gate_Is_Deterministic_And_Heavy_Steps_Are_Split()
    {
        var packageJson = File.ReadAllText(ResolveWorkspaceFile("package.json"));
        var ciGate = File.ReadAllText(ResolveWorkspaceFile("scripts", "ci_gate.ps1"));
        var ciGateHeavy = File.ReadAllText(ResolveWorkspaceFile("scripts", "ci_gate_heavy.ps1"));
        var repoGateWorkflow = File.ReadAllText(ResolveWorkspaceFile(".github", "workflows", "repo-gate.yml"));

        Assert.Contains("\"ci:gate\":", packageJson, StringComparison.Ordinal);
        Assert.Contains("\"ci:gate:heavy\":", packageJson, StringComparison.Ordinal);

        Assert.Contains("run_fast_tests.ps1", ciGate, StringComparison.Ordinal);
        Assert.Contains("run_eval_gate.ps1 -NoBuild -NoRestore", ciGate, StringComparison.Ordinal);
        Assert.Contains("run_tool_benchmark.ps1 -Configuration Debug -NoBuild -NoRestore", ciGate, StringComparison.Ordinal);
        Assert.Contains("monitoring_gate.ps1", ciGate, StringComparison.Ordinal);
        Assert.DoesNotContain("run_load_chaos_smoke.ps1", ciGate, StringComparison.Ordinal);
        Assert.DoesNotContain("run_generation_parity_gate.ps1", ciGate, StringComparison.Ordinal);
        Assert.DoesNotContain("run_generation_parity_benchmark.ps1", ciGate, StringComparison.Ordinal);
        Assert.DoesNotContain("run_generation_parity_window_gate.ps1", ciGate, StringComparison.Ordinal);
        Assert.DoesNotContain("check_backend_control_plane.ps1", ciGate, StringComparison.Ordinal);
        Assert.DoesNotContain("check_latency_budget.ps1", ciGate, StringComparison.Ordinal);
        Assert.DoesNotContain("run_ui_workflow_smoke.ps1", ciGate, StringComparison.Ordinal);
        Assert.DoesNotContain("ui_perf_regression.ps1", ciGate, StringComparison.Ordinal);
        Assert.DoesNotContain("baseline_capture.ps1", ciGate, StringComparison.Ordinal);

        Assert.Contains("run_load_chaos_smoke.ps1", ciGateHeavy, StringComparison.Ordinal);
        Assert.Contains("run_generation_parity_gate.ps1", ciGateHeavy, StringComparison.Ordinal);
        Assert.Contains("run_generation_parity_benchmark.ps1", ciGateHeavy, StringComparison.Ordinal);
        Assert.Contains("run_generation_parity_window_gate.ps1", ciGateHeavy, StringComparison.Ordinal);
        Assert.Contains("check_backend_control_plane.ps1", ciGateHeavy, StringComparison.Ordinal);
        Assert.Contains("check_latency_budget.ps1", ciGateHeavy, StringComparison.Ordinal);
        Assert.Contains("run_ui_workflow_smoke.ps1", ciGateHeavy, StringComparison.Ordinal);
        Assert.Contains("ui_perf_regression.ps1", ciGateHeavy, StringComparison.Ordinal);
        Assert.Contains("baseline_capture.ps1", ciGateHeavy, StringComparison.Ordinal);

        Assert.Contains("Monitoring config", repoGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("Eval gate", repoGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("Tool benchmark", repoGateWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Load/Chaos smoke", repoGateWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Generation parity gate", repoGateWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("UI workflow smoke", repoGateWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Heavy_Report_Scripts_Do_Not_Default_To_Doc_Root_Residue()
    {
        var loadChaosScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "load_streaming_chaos.ps1"));
        var parityGateScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "run_generation_parity_gate.ps1"));
        var parityBenchmarkScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "run_generation_parity_benchmark.ps1"));
        var parityWindowScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "run_generation_parity_window_gate.ps1"));
        var parityNightlyScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "run_generation_parity_nightly.ps1"));

        Assert.Contains("temp/verification/heavy/load_streaming_chaos_report.md", loadChaosScript, StringComparison.Ordinal);
        Assert.DoesNotContain("doc/load_streaming_chaos_report.md", loadChaosScript, StringComparison.Ordinal);

        Assert.Contains("temp/verification/heavy/HELPER_PARITY_GATE_", parityGateScript, StringComparison.Ordinal);
        Assert.DoesNotContain("doc/HELPER_PARITY_GATE_", parityGateScript, StringComparison.Ordinal);

        Assert.Contains("temp/verification/heavy/HELPER_GENERATION_PARITY_BENCHMARK_", parityBenchmarkScript, StringComparison.Ordinal);
        Assert.DoesNotContain("doc/HELPER_GENERATION_PARITY_BENCHMARK_", parityBenchmarkScript, StringComparison.Ordinal);

        Assert.Contains("temp/verification/heavy/HELPER_PARITY_WINDOW_GATE_", parityWindowScript, StringComparison.Ordinal);
        Assert.DoesNotContain("doc/HELPER_PARITY_WINDOW_GATE_", parityWindowScript, StringComparison.Ordinal);

        Assert.Contains("temp/verification/parity_nightly", parityNightlyScript, StringComparison.Ordinal);
        Assert.DoesNotContain("doc/parity_nightly", parityNightlyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Certification_Compile_Real_Build_Smoke_Is_Whitelisted_And_Bounded()
    {
        var compileProject = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Certification.Compile.Tests", "Helper.Runtime.Certification.Compile.Tests.csproj"));
        var promotionTests = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "TemplatePromotionEndToEndAndChaosTests.cs"));
        var certificationTests = File.ReadAllText(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "TemplateCertificationServiceTests.cs"));

        Assert.Contains("CompileLaneTestDoubles.cs", compileProject, StringComparison.Ordinal);
        Assert.Contains("TemplatePromotionEndToEndAndChaosTests.cs", compileProject, StringComparison.Ordinal);
        Assert.Contains("TemplateCertificationServiceTests.cs", compileProject, StringComparison.Ordinal);
        Assert.Contains("DotnetServiceTraceBehaviorTests.cs", compileProject, StringComparison.Ordinal);

        Assert.Contains("PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke", promotionTests, StringComparison.Ordinal);
        Assert.Contains("PromotionPipeline_E2E_RouteToActivation_PassesForStubbedGoldenSuite", promotionTests, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(promotionTests, @"new DotnetService\(").Cast<System.Text.RegularExpressions.Match>());

        Assert.Contains("BuildRealBuildService", certificationTests, StringComparison.Ordinal);
        Assert.Contains("BuildStubbedService", certificationTests, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(certificationTests, @"new DotnetService\(").Cast<System.Text.RegularExpressions.Match>());
    }

    [Fact]
    public void Certification_Compile_Dotnet_Tracing_And_Timeout_Policy_Are_Explicit()
    {
        var dotnetService = File.ReadAllText(ResolveWorkspaceFile("src", "Helper.Runtime", "DotnetService.cs"));
        var timeoutPolicy = File.ReadAllText(ResolveWorkspaceFile("src", "Helper.Runtime", "Infrastructure", "Dotnet", "DotnetTimeoutPolicy.cs"));
        var processRunner = File.ReadAllText(ResolveWorkspaceFile("src", "Helper.Runtime", "Infrastructure", "Dotnet", "DotnetProcessRunner.cs"));
        var resultMapper = File.ReadAllText(ResolveWorkspaceFile("src", "Helper.Runtime", "Infrastructure", "Dotnet", "DotnetProcessResultMapper.cs"));
        var traceWriter = File.ReadAllText(ResolveWorkspaceFile("src", "Helper.Runtime", "Infrastructure", "DotnetProcessTraceWriter.cs"));
        var compileScript = File.ReadAllText(ResolveWorkspaceFile("scripts", "run_certification_compile_tests.ps1"));

        Assert.Contains("DotnetProcessRunner.RunAsync", dotnetService, StringComparison.Ordinal);
        Assert.Contains("DotnetProcessResultMapper", dotnetService, StringComparison.Ordinal);
        Assert.Contains("HELPER_DOTNET_BUILD_TIMEOUT_SEC", timeoutPolicy, StringComparison.Ordinal);
        Assert.Contains("HELPER_DOTNET_TEST_TIMEOUT_SEC", timeoutPolicy, StringComparison.Ordinal);
        Assert.Contains("HELPER_DOTNET_RESTORE_TIMEOUT_SEC", timeoutPolicy, StringComparison.Ordinal);
        Assert.Contains("kill_confirmed", processRunner, StringComparison.Ordinal);
        Assert.Contains("orphan_risk", processRunner, StringComparison.Ordinal);
        Assert.Contains("GENERATION_STAGE_TIMEOUT", resultMapper, StringComparison.Ordinal);

        Assert.Contains("certification_process_trace.jsonl", traceWriter, StringComparison.Ordinal);
        Assert.Contains("HELPER_CERTIFICATION_PROCESS_TRACE_PATH", traceWriter, StringComparison.Ordinal);

        Assert.Contains("Stop-LaneProcesses", compileScript, StringComparison.Ordinal);
        Assert.Contains("Remove-TempResidue", compileScript, StringComparison.Ordinal);
        Assert.Contains("Release-LaneLock", compileScript, StringComparison.Ordinal);
    }
}
