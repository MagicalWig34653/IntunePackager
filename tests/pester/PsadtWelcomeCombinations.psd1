# The parameter combinations that deploy/psadt/template/Invoke-AppDeployToolkit.ps1 builds for commands of the toolkit.
# PsadtEngine.Tests.ps1 checks each one against the parameter sets of the real toolkit and checks that the script uses no other
# parameter in the splats $welcome and $prompt. 'Timeout' of the prompt is a dynamic parameter of the toolkit and not listed.
@{ Combinations = @(
    @{ Command = 'Show-ADTInstallationWelcome'; Name = 'only a disk space check'; Names = @('CheckDiskSpace') }
    @{ Command = 'Show-ADTInstallationWelcome'; Name = 'only a disk space check with a size'; Names = @('CheckDiskSpace', 'RequiredDiskSpace') }
    @{ Command = 'Show-ADTInstallationPrompt'; Name = 'ask to close programs'; Names = @('Message', 'ButtonLeftText', 'PersistPrompt', 'NoExitOnTimeout') }
    @{ Command = 'Show-ADTInstallationPrompt'; Name = 'ask to close programs, postponing allowed'; Names = @('Message', 'ButtonLeftText', 'ButtonRightText', 'PersistPrompt', 'NoExitOnTimeout') }
) }
