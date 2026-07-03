# PowerShell script to publish the worker and build the WiX MSI installer

Write-Host "Publishing InterviewAudit.Worker in Release mode..." -ForegroundColor Cyan
dotnet publish ..\InterviewAudit.Worker\InterviewAudit.Worker.csproj -c Release -r win-x64 --self-contained false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish worker service."
    exit 1
}

Write-Host "Building MSI installer using WiX Toolset..." -ForegroundColor Cyan

# Wix 4/5 build command
wix build Product.wxs -o InterviewAuditSetup.msi

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build MSI installer using WiX."
    exit 1
}

Write-Host "MSI Installer built successfully: InterviewAuditSetup.msi" -ForegroundColor Green
