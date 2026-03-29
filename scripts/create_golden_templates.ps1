Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")
$pathConfig = Get-HelperPathConfig -WorkspaceRoot (Join-Path $PSScriptRoot "..")
$dataRoot = $pathConfig.DataRoot
$basePath = $pathConfig.TemplatesRoot

$templates = @(
    @{ Id="Template_EnterpriseCRMLite"; Name="Enterprise CRM Lite"; Lang="csharp"; Type="wpf" },
    @{ Id="Template_SystemHealthMonitor"; Name="System Health & Performance Monitor"; Lang="csharp"; Type="wpf" },
    @{ Id="Template_SecureVault"; Name="Secure Vault"; Lang="csharp"; Type="wpf" },
    @{ Id="Template_IdePluginStarter"; Name="Custom IDE Plugin Starter"; Lang="csharp"; Type="console" },
    @{ Id="Template_AiAnalyticsDashboard"; Name="AI-Analytics Dashboard"; Lang="typescript"; Type="react" },
    @{ Id="Template_DistributedTaskScheduler"; Name="Distributed Task Scheduler"; Lang="javascript"; Type="node" },
    @{ Id="Template_MicroservicesGateway"; Name="Microservices Gateway"; Lang="javascript"; Type="node" },
    @{ Id="Template_EcommerceStorefront"; Name="E-commerce Storefront Starter"; Lang="typescript"; Type="react" },
    @{ Id="Template_AiCodeReviewer"; Name="AI Code Reviewer"; Lang="csharp"; Type="console" },
    @{ Id="Template_VectorSearchEngine"; Name="Vector Search Engine"; Lang="python"; Type="fastapi" },
    @{ Id="Template_PersonalKnowledgeWiki"; Name="Personal Knowledge Wiki"; Lang="python"; Type="python" },
    @{ Id="Template_VoiceIntentAssistant"; Name="Voice Intent Assistant"; Lang="python"; Type="python" },
    @{ Id="Template_AdvancedWebScraper"; Name="Advanced Web Scraper"; Lang="python"; Type="python" },
    @{ Id="Template_ImageProcessingSuite"; Name="Image Processing Suite"; Lang="python"; Type="python" },
    @{ Id="Template_FinancialPortfolioTracker"; Name="Financial Portfolio Tracker"; Lang="python"; Type="python" },
    @{ Id="Template_LogAggregator"; Name="Log Aggregator & Audit Service"; Lang="csharp"; Type="console" },
    @{ Id="Template_MultiProtocolBridge"; Name="Multi-Protocol Bridge"; Lang="javascript"; Type="node" }
)

foreach ($t in $templates) {
    $dir = "$basePath\$($t.Id)"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    
    Write-Host "Creating template: $($t.Id)..."

    # template.json
    $json = @"
{
  "Id": "$($t.Id)",
  "Name": "$($t.Name)",
  "Description": "Golden template for $($t.Name). Initial scaffold.",
  "Language": "$($t.Lang)",
  "Tags": ["golden", "$($t.Type)"]
}
"@
    Set-Content -Path "$dir\template.json" -Value $json -Encoding UTF8

    # Scaffolding based on Type
    if ($t.Type -eq "wpf") {
        $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
"@
        Set-Content -Path "$dir\Project.csproj" -Value $csproj -Encoding UTF8
        
        $appXaml = @"
<Application x:Class="GoldenApp.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
"@
        Set-Content -Path "$dir\App.xaml" -Value $appXaml -Encoding UTF8
        
        $appCs = @"
using System.Windows;
namespace GoldenApp { public partial class App : Application { } }
"@
        Set-Content -Path "$dir\App.xaml.cs" -Value $appCs -Encoding UTF8
        
        $mainXaml = @"
<Window x:Class="GoldenApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="$($t.Name)" Height="450" Width="800">
    <Grid>
        <TextBlock Text="Hello from $($t.Name)!" HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Grid>
</Window>
"@
        Set-Content -Path "$dir\MainWindow.xaml" -Value $mainXaml -Encoding UTF8
        
        $mainCs = @"
using System.Windows;
namespace GoldenApp { public partial class MainWindow : Window { public MainWindow() { InitializeComponent(); } } }
"@
        Set-Content -Path "$dir\MainWindow.xaml.cs" -Value $mainCs -Encoding UTF8

    } elseif ($t.Type -eq "console") {
        $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
"@
        Set-Content -Path "$dir\Project.csproj" -Value $csproj -Encoding UTF8
        $prog = @"
Console.WriteLine("Hello from $($t.Name)!");
"@
        Set-Content -Path "$dir\Program.cs" -Value $prog -Encoding UTF8

    } elseif ($t.Type -eq "react") {
        $pkg = @"
{
  "name": "$($t.Id.ToLower())",
  "version": "1.0.0",
  "type": "module",
  "scripts": { "dev": "vite", "build": "tsc && vite build" },
  "dependencies": { "react": "^18.2.0", "react-dom": "^18.2.0" },
  "devDependencies": { "vite": "^5.0.0", "typescript": "^5.0.0" }
}
"@
        Set-Content -Path "$dir\package.json" -Value $pkg -Encoding UTF8
        $html = @"
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <title>$($t.Name)</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
"@
        Set-Content -Path "$dir\index.html" -Value $html -Encoding UTF8
        New-Item -ItemType Directory -Force -Path "$dir\src" | Out-Null
        $appTsx = @"
import React from 'react';
export default function App() { return <div><h1>$($t.Name)</h1></div>; }
"@
        Set-Content -Path "$dir\src\App.tsx" -Value $appTsx -Encoding UTF8
        $mainTsx = @"
import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
ReactDOM.createRoot(document.getElementById('root')!).render(<App />);
"@
        Set-Content -Path "$dir\src\main.tsx" -Value $mainTsx -Encoding UTF8

    } elseif ($t.Type -eq "node") {
        $pkg = @"
{
  "name": "$($t.Id.ToLower())",
  "version": "1.0.0",
  "main": "index.js",
  "scripts": { "start": "node index.js" }
}
"@
        Set-Content -Path "$dir\package.json" -Value $pkg -Encoding UTF8
        $idx = @"
console.log('Starting $($t.Name)...');
"@
        Set-Content -Path "$dir\index.js" -Value $idx -Encoding UTF8

    } elseif ($t.Type -eq "python" -or $t.Type -eq "fastapi") {
        $req = ""
        $py = ""
        if ($t.Type -eq "fastapi") {
            $req = "fastapi`nuvicorn"
            $py = @"
from fastapi import FastAPI
app = FastAPI()

@app.get('/')
def read_root():
    return {'message': 'Welcome to $($t.Name)'}

if __name__ == '__main__':
    import uvicorn
    uvicorn.run(app, host='0.0.0.0', port=8000)
"@
        } else {
            $py = @"
def main():
    print('Running $($t.Name)...')

if __name__ == '__main__':
    main()
"@
        }
        Set-Content -Path "$dir\requirements.txt" -Value $req -Encoding UTF8
        Set-Content -Path "$dir\main.py" -Value $py -Encoding UTF8
    }
}
Write-Host "All 17 templates created successfully!"
