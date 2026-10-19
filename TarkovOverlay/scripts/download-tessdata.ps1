$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
# tessdata_fast — автоскан всего экрана (быстро); tessdata_best — подсказка у курсора (точнее)
$sets = @{ "tessdata_fast" = "https://github.com/tesseract-ocr/tessdata_fast/raw/main";
           "tessdata"      = "https://github.com/tesseract-ocr/tessdata_best/raw/main" }
foreach ($folder in $sets.Keys) {
    $dir = Join-Path $PSScriptRoot "..\$folder"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    foreach ($file in @("rus.traineddata", "eng.traineddata")) {
        $dest = Join-Path $dir $file
        if (Test-Path $dest) { Write-Host "Уже есть: $folder\$file"; continue }
        Write-Host "Скачиваю $folder\$file..."
        Invoke-WebRequest -Uri "$($sets[$folder])/$file" -OutFile $dest
    }
}
Write-Host "Готово"
