$content = [System.IO.File]::ReadAllText("C:\Users\Jeik\Documents\Repos\UXTU\Universal x86 Tuning Utility\Views\Pages\FlydigiCooler.xaml")
$lines = $content -split "`n"
for ($i = 20; $i -lt 25; $i++) {
    $visual = $lines[$i].Replace(" ", "[SP]").Replace("`t", "[TAB]")
    Write-Host "Line $($i+1): $visual"
}
