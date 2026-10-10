# The parameter combinations that Request-PsadtClose in deploy/psadt/template/Invoke-AppDeployToolkit.ps1 can build for
# Show-ADTInstallationWelcome. PsadtEngine.Tests.ps1 checks each one against the parameter sets of the real toolkit and checks
# that the script uses no other parameter.
@{ Combinations = @(
    @{ Name = 'close programs'; Names = @('CloseProcesses', 'HideCloseButton', 'PersistPrompt') }
    @{ Name = 'close programs with postponing'; Names = @('CloseProcesses', 'HideCloseButton', 'PersistPrompt', 'AllowDeferCloseProcesses', 'DeferTimes') }
    @{ Name = 'close programs with blocking'; Names = @('CloseProcesses', 'HideCloseButton', 'PersistPrompt', 'BlockExecution') }
    @{ Name = 'close programs with a disk space check'; Names = @('CloseProcesses', 'HideCloseButton', 'PersistPrompt', 'CheckDiskSpace') }
    @{ Name = 'close programs with a disk space check and a size'; Names = @('CloseProcesses', 'HideCloseButton', 'PersistPrompt', 'CheckDiskSpace', 'RequiredDiskSpace') }
    @{ Name = 'everything'; Names = @('CloseProcesses', 'HideCloseButton', 'PersistPrompt', 'AllowDeferCloseProcesses', 'DeferTimes', 'BlockExecution', 'CheckDiskSpace', 'RequiredDiskSpace') }
    @{ Name = 'only a disk space check'; Names = @('CheckDiskSpace') }
    @{ Name = 'only a disk space check with a size'; Names = @('CheckDiskSpace', 'RequiredDiskSpace') }
) }
