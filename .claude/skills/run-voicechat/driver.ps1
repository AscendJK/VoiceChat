# VoiceChat WPF App Driver
# PowerShell driver for automated interaction with the VoiceChat WPF application
# Uses UIAutomation to find and interact with UI elements

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

function Get-VoiceChatProcess {
    return Get-Process -Name "VoiceChat.App" -ErrorAction SilentlyContinue
}

function Invoke-VoiceChatLaunch {
    param(
        [string]$Path = "E:\ClaudeCode\VoiceChat\publish\VoiceChat.App.exe"
    )

    $existing = Get-VoiceChatProcess
    if ($existing) {
        Write-Output "App already running (PID: $($existing.Id))"
        return $existing
    }

    Write-Output "Launching VoiceChat from: $Path"
    $process = Start-Process -FilePath $Path -PassThru
    Start-Sleep -Seconds 3
    return $process
}

function Stop-VoiceChat {
    $processes = Get-VoiceChatProcess
    if ($processes) {
        foreach ($p in $processes) {
            Write-Output "Stopping VoiceChat (PID: $($p.Id))..."
            $p.CloseMainWindow() | Out-Null
        }
        Start-Sleep -Seconds 1
        $remaining = Get-VoiceChatProcess
        if ($remaining) {
            $remaining | Stop-Process -Force
        }
        Write-Output "VoiceChat stopped."
    } else {
        Write-Output "VoiceChat is not running."
    }
}

function Get-VoiceChatMainWindow {
    $processes = Get-VoiceChatProcess
    if (!$processes) { return $null }

    $window = $null
    foreach ($p in $processes) {
        $h = $p.MainWindowHandle
        if ($h -ne [IntPtr]::Zero) {
            try {
                $ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
                if ($ae) {
                    $window = $ae
                }
            } catch {
                # skip
            }
        }
    }
    return $window
}

function Get-VoiceChatScreenshot {
    param(
        [string]$Path = "E:\ClaudeCode\VoiceChat\voicechat_screenshot.png",
        [switch]$WindowOnly
    )

    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Output "Screenshot saved to: $Path"
}

function Invoke-VoiceChatAction {
    param(
        [string]$ButtonName
    )

    $window = Get-VoiceChatMainWindow
    if (!$window) {
        Write-Output "VoiceChat window not found!"
        return $false
    }

    $condition = [System.Windows.Automation.Condition]::TrueCondition
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)

    foreach ($element in $all) {
        $name = $element.Current.Name
        $ctrlTypeId = $element.Current.ControlType.Id
        # 50000 = Button control type id
        if ($ctrlTypeId -eq 50000 -and $name -like "*$ButtonName*") {
            Write-Output "Found button: '$name' - Clicking..."
            try {
                $invoke = [System.Windows.Automation.InvokePattern]::Get($element)
                $invoke.Invoke()
                return $true
            } catch {
                Write-Output "Could not invoke button: $_"
                return $false
            }
        }
    }

    Write-Output "Button matching '$ButtonName' not found."
    return $false
}

function Get-VoiceChatRoomList {
    $window = Get-VoiceChatMainWindow
    if (!$window) { return @() }

    $roomElements = @()
    $condition = [System.Windows.Automation.Condition]::TrueCondition
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)

    foreach ($element in $all) {
        $name = $element.Current.Name
        $ctrlTypeId = $element.Current.ControlType.Id
        # 50020 = Text control type id
        if ($ctrlTypeId -eq 50020 -and $name.length -gt 5 -and $name -notlike "*#*" -and $name -notlike "*%*" -and $name -notlike "*???*") {
            $roomElements += $name
        }
    }

    return $roomElements
}