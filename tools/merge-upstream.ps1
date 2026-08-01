#requires -version 5.1
<#
    Слияние с апстримом KomushKrya/0-41 по заранее принятому правилу:
    ядро наше, тексты и ассеты — их.

    Почему скриптом, а не руками: конфликтуют полсотни файлов, среди них
    ShiftDirector на полторы тысячи строк. Разбирать такое построчно в консоли
    невозможно, а решение по каждому файлу всё равно принимается по его пути,
    а не по содержимому.

    Ничего не коммитит. Останавливается перед коммитом, чтобы можно было
    посмотреть результат и передумать: git merge --abort вернёт всё назад.
#>

$ErrorActionPreference = 'Stop'

try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
}
catch {
}

$корень = Split-Path -Parent $PSScriptRoot
Set-Location $корень

function Скажи {
    param([string]$Текст, [string]$Цвет = 'Gray')
    Write-Host $Текст -ForegroundColor $Цвет
}

<#
    Наше — всё, что относится к ядру и к тому, что от него зависит.

    Ядро у нас ушло далеко вперёд: у них GameSession и RadioEncounter, у нас
    KonturSimulation, сохранения, модель Dispatch и зоны. Брать их версию хоть
    одного файла отсюда — значит получить ядро, которое не соберётся.
#>
$наши = @(
    'kontur-core/',
    'content/engine/',
    'scripts/kontur/',
    'scripts/ui/ContentTextBox.cs',
    'scripts/debug/KonturDebugOverlay.cs',
    'scripts/debug/ContentHotReload.cs',
    'docs/',
    'data/',
    'project.godot',
    'ДИЗАЙН-ДОКУМЕНТ'
)

<#
    Их — проза. Тексты пишет сценарист, и его версия свежее нашей копии:
    мы переносили их из выгрузки, а команда с тех пор правила дальше.
#>
$ихние = @(
    'content/raw/'
)

<#
    Файлы старого ядра. Придут из апстрима и будут дублировать наш фасад:
    два входа в симуляцию разом — это не соберётся.
#>
$наУдаление = @(
    'kontur-core/src/Kontur.Core/Api/GameSession.cs',
    'scripts/kontur/GameRuntime.cs',
    'scripts/kontur/GameRuntime.cs.uid'
)

function ЭтоНаше {
    param([string]$Путь)
    foreach ($префикс in $наши) {
        if ($Путь -like "$префикс*") { return $true }
    }
    return $false
}

function ЭтоИхнее {
    param([string]$Путь)
    foreach ($префикс in $ихние) {
        if ($Путь -like "$префикс*") { return $true }
    }
    return $false
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  Слияние с апстримом: ядро наше, тексты их' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''

# --- Проверки перед началом ---------------------------------
if (-not (Test-Path (Join-Path $корень 'project.godot'))) {
    Скажи '[ОШИБКА] Запускать из корня проекта.' 'Red'
    return 1
}

& git rev-parse --verify --quiet upstream/main > $null 2>&1
if ($LASTEXITCODE -ne 0) {
    Скажи '[ОШИБКА] Не вижу upstream/main. Сначала:' 'Red'
    Скажи '    git remote add upstream https://github.com/KomushKrya/0-41.git'
    Скажи '    git fetch upstream'
    return 1
}

# Незавершённое слияние с прошлого раза — вычищаем, иначе намешаем.
if (Test-Path (Join-Path $корень '.git\MERGE_HEAD')) {
    Скажи 'Найдено незавершённое слияние — отменяю его.' 'Yellow'
    & git merge --abort
    Скажи ''
}

$ветка = (& git rev-parse --abbrev-ref HEAD).Trim()
Скажи "Ветка: $ветка"

$грязно = (& git status --porcelain)
if ($грязно) {
    Скажи ''
    Скажи '[ОШИБКА] В рабочей папке есть несохранённые изменения.' 'Red'
    Скажи 'Закоммитьте или отложите их: слияние затрёт незакоммиченное.'
    return 1
}

# --- Слияние ------------------------------------------------
Скажи ''
Скажи 'Сливаю upstream/main...'
& git merge --no-commit --no-ff upstream/main 2>&1 | Out-Null

$конфликты = @(& git diff --name-only --diff-filter=U)
if ($конфликты.Count -eq 0) {
    Скажи 'Конфликтов нет — можно просто закоммитить.' 'Green'
    return 0
}

Скажи "Конфликтов: $($конфликты.Count)" 'Yellow'
Скажи ''

# --- Разбор по правилам -------------------------------------
$счётНаших = 0
$счётИхних = 0
$неразобранные = @()

foreach ($файл in $конфликты) {
    if (ЭтоНаше $файл) {
        # --ours может не сработать, если файл удалён на нашей стороне;
        # тогда просто фиксируем то, что лежит в дереве.
        & git checkout --ours -- $файл 2>$null
        & git add -- $файл 2>$null
        $счётНаших++
    }
    elseif (ЭтоИхнее $файл) {
        & git checkout --theirs -- $файл 2>$null
        & git add -- $файл 2>$null
        $счётИхних++
    }
    else {
        $неразобранные += $файл
    }
}

Скажи "Взято наших:  $счётНаших" 'Cyan'
Скажи "Взято ихних:  $счётИхних" 'Cyan'

if ($неразобранные.Count -gt 0) {
    Скажи ''
    Скажи 'Под правило не подошли — разберите вручную:' 'Yellow'
    foreach ($файл in $неразобранные) {
        Скажи "    $файл"
    }
}

# --- Удаление файлов старого ядра ---------------------------
Скажи ''
foreach ($файл in $наУдаление) {
    if (Test-Path (Join-Path $корень $файл)) {
        & git rm -f --quiet -- $файл 2>$null
        Скажи "Удалён файл старого ядра: $файл" 'DarkGray'
    }
}

# --- Итог ---------------------------------------------------
Скажи ''
Скажи '============================================================' 'Cyan'
Скажи '  Слияние подготовлено, но НЕ закоммичено.' 'Cyan'
Скажи '============================================================' 'Cyan'
Скажи ''
Скажи 'Дальше:'
Скажи '  git status --short      посмотреть, что получилось'
Скажи '  git merge --abort       передумать и вернуть всё назад'
Скажи ''
Скажи 'Коммитить пока рано: сначала надо собрать ядро и прогнать проверки.' 'Yellow'

return 0
