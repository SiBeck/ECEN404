param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$MrdPath,
    [Parameter(Mandatory = $false, Position = 1)]
    [string]$OutputPath
)

$inputFile = Resolve-Path -LiteralPath $MrdPath -ErrorAction Stop

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    python main.py mrd-to-png "$inputFile"
}
else {
    python main.py mrd-to-png "$inputFile" --output "$OutputPath"
}
