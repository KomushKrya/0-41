# Ядро К.О.Н.Т.У.Р. — руководство для интерфейса

Документ для того, кто прикручивает предметы на столе к симуляционному ядру.
Всё, что здесь описано, находится в сборке `Kontur.Core` и работает без Godot.

> **Прозы в событиях нет.** Ядро рассылает только идентификаторы: `CallId`, `MissionEventId`,
> `ReportId`, `ShiftNoteId`, `CreatureId`, `PropertyId`. Текст под ними разворачивает
> текстовый движок — см. `docs/ТЕКСТОВЫЕ-БОКСЫ.md`. Если вам в сцене нужна строка,
> вы берёте её по id, а не из события.

Исходники ядра: `kontur-core/src/Kontur.Core/` (в дереве Godot не видны — папка скрыта
через `.gdignore`, открывайте её внешним редактором или через `kontur-core/Kontur.Core.sln`).

---

## 1. Как это устроено

Ядро — детерминированный автомат. У него ровно три входа и один выход:

```
   команды игрока  ──┐
   Tick(delta)  ─────┤──►  KonturSimulation  ──►  поток событий (Event Bus)
   контент (JSON) ───┘
```

**Внутрь — только команды.** Интерфейс не публикует события в ядро, он вызывает методы
(`AnswerCall`, `DispatchSquad`, …). Каждый возвращает `CommandResult` с явной причиной отказа.

**Наружу — только события.** Всё, что произошло, приходит подписчику сигналом.
Опрашивать состояние в `_Process` не нужно и не следует.

**Снимки — для отрисовки.** Методы `Get*` возвращают неизменяемые записи. Ими
пользуются, когда нужно нарисовать список целиком (открыть досье, перерисовать карту),
а не чтобы ловить изменения.

Практическое следствие: один `Seed` плюс одна последовательность действий игрока дают
один и тот же прогон смены. Баг воспроизводится, а не «иногда случается».

---

## 2. Быстрый старт

Ядро уже поднято автозагрузкой `Kontur` (`scripts/kontur/KonturRuntime.cs`).
Из любой сцены:

```csharp
public partial class DeskPhone : Node3D
{
    private KonturRuntime _kontur;
    private IDisposable _incidentSubscription;
    private string _activeIncidentId;

    public override void _Ready()
    {
        _kontur = KonturRuntime.Get(this);
        if (_kontur == null || !_kontur.IsReady)
        {
            GD.PushError("Ядро недоступно: " + (_kontur?.LoadError ?? "автозагрузка Kontur не найдена"));
            return;
        }

        _incidentSubscription = _kontur.Simulation.Events.Subscribe<IncidentCreated>(OnIncidentCreated);
    }

    public override void _ExitTree()
    {
        // Обязательно: подписка живёт дольше сцены и утащит за собой ссылку на удалённый узел.
        _incidentSubscription?.Dispose();
    }

    private void OnIncidentCreated(IncidentCreated e)
    {
        _activeIncidentId = e.IncidentId;

        // e.CallId — запись типа call. Реплики звонящего и служебную шапку
        // («ЗВОНОК ПЕРЕНАПРАВЛЕН ИЗ ЖЭК») развернёт текстовый бокс:
        //     _callBox.Open(e.CallId);
        StartRinging(e.RingSeconds);
    }

    private void OnHandsetPickedUp()
    {
        CommandResult result = _kontur.Simulation.AnswerCall(_activeIncidentId);
        if (!result.IsSuccess)
        {
            GD.Print("Отказ: " + result.Error);
        }
    }
}
```

Нужные `using`:

```csharp
using Kontur.Core.Api;      // KonturSimulation, CommandResult, все View-типы
using Kontur.Core.Events;   // сигналы и IEventBus
using Kontur.Core.Model;    // StatBlock, IncidentPhase, EmployeeStatus, ScaleValues …
```

---

## 3. Шесть правил, которые экономят время

1. **Всегда отписывайтесь в `_ExitTree()`.** `Subscribe` возвращает `IDisposable`.
2. **Не храните состояние игры в сцене.** Кэшировать для отрисовки можно, истина — в ядре.
   Вызовы накладываются, и рассинхрон вылезет именно на втором одновременном вызове.
3. **Все команды принимают `IncidentId`,** а не «текущий вызов». Текущего вызова не бывает:
   одновременно могут звонить телефон по одному инциденту, висеть метка по второму
   и трещать радио по третьему.
4. **Не читайте `Simulation.DebugState`.** Это внутренности для отладочного оверлея.
   Для интерфейса есть `Get*`.
5. **Длительность таймера `0` означает «без ограничения».** В обучающей смене таймеры
   игрока отключены — не рисуйте обратный отсчёт, если в событии пришёл ноль.
6. **Текст берётся по id, а не из события.** В сигналах лежат `CallId`, `MissionEventId`,
   `ReportId`, `ShiftNoteId` — отдайте их `ContentTextBox.Open(id)`. Склеивать строки
   в C# и держать прозу в `.tscn` не нужно и нельзя.

---

## 4. Команды

Все возвращают `CommandResult` — `IsSuccess` и `Error` (человекочитаемая причина отказа).
Ядро никогда не бросает исключения на действия игрока.

| Команда | Когда допустима | Порождает |
|---|---|---|
| `StartShift(day)` | смена не идёт, партия не проиграна | `ShiftStarted` |
| `AnswerCall(id)` | фаза `Ringing` | `CallAnswered` |
| `ConfirmBriefing(id)` | фаза `Briefing` | `MapMarkerSpawned` |
| `OpenDispatchScreen(id)` | фаза `MarkerActive` | `DispatchScreenRequested`, `TimeFreezeChanged(true)` |
| `CloseDispatchScreen(id)` | всегда | `DispatchScreenClosed`, `TimeFreezeChanged(false)` |
| `AnswerRadio(id)` | фаза `RadioPending` | `RadioAnswered`, `TimeFreezeChanged(true)` |
| `CloseRadio(id)` | всегда | `TimeFreezeChanged(false)` |
| `OpenMissionOutcome(id)` | всегда | `TimeFreezeChanged(true)` |
| `CloseMissionOutcome(id)` | всегда | `TimeFreezeChanged(false)` |
| `DispatchSquad(id, employeeIds, equipmentIds)` | фаза `MarkerActive` | `SquadDispatched` |
| `ChooseRadioOption(id, optionId)` | фаза `RadioPending` | `RadioOptionChosen` |
| `SetFlag(flag, value)` / `ToggleFlag(flag)` | всегда | `FlagChanged` |
| `GetRadioOptions(id)` | всегда | — (снимок доступности вариантов) |
| `SpendSkillPoint(employeeId, stat)` | есть нераспределённые очки | `EmployeeStatsChanged` |
| `HireEmployee(candidateId, day)` | смена не идёт, лимит штата не исчерпан | `EmployeeHired` |
| `ConfirmStartingRoster(ids)` | смена не идёт, состав ещё не собран | `EmployeeHired` на каждого |
| `ForceEndShift()` | всегда | `ShiftEnded`, `HiringOpened` |
| `ResetToNewGame()` | всегда | — (см. ниже) |
| `Save(label)` | всегда, в том числе посреди смены | — (возвращает JSON) |
| `Load(json)` | всегда | `GameLoaded`, `TimeFreezeChanged(true)` |
| `ResumeAfterLoad()` | после `Load` | `TimeFreezeChanged(false)` |
| `Tick(deltaSeconds)` | каждый кадр | зависит от фазы |

### `ResetToNewGame` — полный сброс партии

Обрывает текущую смену **без** события `ShiftEnded`: смена не доработана, а отменена,
и интерфейс не должен принимать это за окончание дня и включать ролик. Затем чистится
всё состояние: штат заново клонируется из контента, шкалы возвращаются к стартовым,
энциклопедия и склад опустошаются, зоны сбрасываются к исходной штриховке.

События при этом почти не публикуются — перерисуйте интерфейс по `Get*`-снимкам сами.

### Отказы `DispatchSquad`

Проверяются по порядку, до первой ошибки:

- метка не активна → «Отправка возможна только пока метка активна»;
- пустой список сотрудников;
- сотрудник указан дважды;
- сотрудник не найден / погиб / уже на выезде;
- превышены слоты: 1 под обычное или сюжетное, 2 под расходники;
- предмета нет на складе.

Команда атомарна: если снаряжения не хватило, уже занятые предметы возвращаются на склад,
и группа никуда не уходит.

### `EstimateDispatch` — предпросмотр для экрана отправки

```csharp
DispatchEstimateView estimate = simulation.EstimateDispatch(incidentId, employeeIds, equipmentIds);
// estimate.Requirements   — пороги с учётом дня, штриховки зоны и выбранного радио-варианта
// estimate.SquadStats     — профиль группы: лучший по каждой характеристике
// estimate.Matches        — построчно: что нужно, кто закрывает, какая ступень
// estimate.MatchScore     — 0..1, главная характеристика весит вдвое
// estimate.SuccessChance  — 0..1
// estimate.IsPerfectMatch — все ступени зелёные, броска не будет
```

Побочных эффектов нет: кубик не бросается, склад не трогается. Можно дёргать на каждое
переключение галочки. Считает тот же код, что и настоящий резолв, поэтому экран отправки
физически не может разойтись с итогом миссии.

`Matches` — то, ради чего экран отправки существует: строка на каждый порог с оценкой
`Exceeds` / `Meets` / `Below`, которую интерфейс красит зелёным, жёлтым и красным.
`IsPrimary` помечает главную характеристику вызова, `Shortfall` говорит, сколько не хватает.

Показывать игроку пороги и профиль группы — нормально. Показывать точный процент —
на усмотрение дизайна: по ДД (раздел 8) прямых подсказок в интерфейсе быть не должно.

---

## 5. События

### Смена

| Событие | Поля | Кто реагирует |
|---|---|---|
| `ShiftStarted` | `Day`, `StaffLimit`, `ShiftNoteId` | записка сменщика: `Open(ShiftNoteId)` |
| `CallWindowClosed` | `Day`, `OpenIncidents` | необязательно: подсказка «новых вызовов не будет» |
| `ShiftEnded` | `Day`, `OutroCutsceneId`, `Summary` | менеджер сцен → пререндеренный ролик |
| `GameOverTriggered` | `Reason`, `Values`, `Day` | финальный экран под причину |
| `TimeFreezeChanged` | `IsFrozen`, `Reason` | гасить обратные отсчёты, замораживать анимации |

`Summary` — `ShiftSummary(TotalIncidents, Successes, Failures, MissedCalls, ExpiredMarkers, Injuries, Deaths)`.

### Телефон

| Событие | Поля | Что делает интерфейс |
|---|---|---|
| `IncidentQueued` | `IncidentId`, `CallId`, `Position` | индикатор «на линии ждут»; телефон ещё **не** звонит |
| `IncidentCreated` | `IncidentId`, `MissionId`, `ZoneId`, `BuildingId`, `CallId`, `RingSeconds` | начать звонок, запустить индикатор |
| `CallAnswered` | `IncidentId`, `MissionId`, `CallId` | `Open(CallId)` — реплики и шапка звонка |
| `CallMissed` | `IncidentId`, `MissionId` | оборвать звонок |

### Карта

| Событие | Поля | Что делает интерфейс |
|---|---|---|
| `MapMarkerSpawned` | `IncidentId`, `ZoneId`, `BuildingId`, `LifetimeSeconds` | поставить метку, индикатор |
| `MapMarkerExpired` | `IncidentId`, `ZoneId`, `BuildingId` | снять метку |
| `SquadDispatched` | `IncidentId`, `EmployeeIds`, `EquipmentIds`, `TravelSeconds` | пунктирная линия к точке |
| `SquadArrived` | `IncidentId`, `ZoneId`, `BuildingId` | линия дошла |
| `SquadReturned` | `IncidentId`, `EmployeeIds` | линия обратно, убрать метку |
| `ZoneStateChanged` | `ZoneId`, `OldState`, `NewState`, `Reason` | переключить слой штриховки |

Прогресса маршрута ядро не отдаёт: берите `TravelSeconds` из события и интерполируйте
у себя, либо `IncidentView.RemainingSeconds` в фазе `Travelling`.

### Куда ставить метку: дом и район

У вызова два адреса, и это не дублирование.

**`ZoneId` — район.** Механический слой: штриховка, вес в подборе вызовов, надбавка
к требованиям. У зоны есть `MapX`/`MapY` — условная точка для отрисовки.

**`BuildingId` — конкретный дом** из `data/buildings.json`, тот самый, что нарисован
геометрией карты. Выбирается заново каждую смену, поэтому живёт на инциденте, а не
на миссии: один и тот же вызов дважды не придёт из одного подъезда.

```csharp
private void OnMarker(MapMarkerSpawned e)
{
    if (!string.IsNullOrEmpty(e.BuildingId) && _buildings.TryGetValue(e.BuildingId, out Node2D house))
    {
        PlaceMarker(house.Position);
        return;
    }

    // Домов в контенте нет — падаем на координаты района.
    ZoneView zone = FindZone(e.ZoneId);
    PlaceMarker(new Vector2((float)zone.MapX, (float)zone.MapY));
}
```

**`BuildingId` может быть пустым** — это не ошибка. Пока в `buildings.json` пусто или
свободных домов не осталось, ядро отдаёт пустую строку, и метку надо ставить по зоне.
Ветку с зоной стоит написать сразу: она же работает в отладочных прогонах, где карты нет.

Два вызова за смену в один дом не попадут. Если у дома проставлен `zoneId`, планировщик
выбирает только среди домов нужного района; если ни у одного дома района нет —
берёт любой свободный. Сейчас районы у домов не проставлены, и работает именно
вторая ветка: дом будет случайным по всей карте.

### Компьютер

| Событие | Поля | Что делает интерфейс |
|---|---|---|
| `DispatchScreenRequested` | `IncidentId`, `MissionId` | открыть экран отправки |
| `DispatchScreenClosed` | `IncidentId` | закрыть экран, ничего не отправив |
| `MissionReportReady` | `Report` | `Open(Report.ReportId)` |
| `CreatureIdentified` | `CreatureId` | новая карточка в энциклопедии |
| `CreatureRevealed` | `CreatureId`, `PropertyId` | открыть абзац под это свойство |

В обоих событиях **прозы нет намеренно**: имя существа и абзацы статьи лежат в
текстовом движке под тем же id. Разворачивает их интерфейс — см.
[ТЕКСТОВЫЕ-БОКСЫ.md](ТЕКСТОВЫЕ-БОКСЫ.md).

### Радиостанция

| Событие | Поля | Что делает интерфейс |
|---|---|---|
| `RadioTriggered` | `IncidentId`, `MissionEventId`, `Options`, `ResponseSeconds` | треск, `Open(MissionEventId)`, кнопки по `Options` |
| `RadioAnswered` | `IncidentId`, `MissionEventId` | открыть экран вариантов |
| `RadioMissed` | `IncidentId` | погасить экран |
| `RadioOptionChosen` | `IncidentId`, `MissionEventId`, `OptionId` | подтверждение выбора |

`Options` — список `RadioOptionOffer(Id, IsUnlocked, Requirements, Shortfall)` в порядке файла.
Закрытый вариант **показывается неактивным с причиной**: `Shortfall` говорит, чего именно
не хватило группе. Это не подсказка о правильности — это цена решения, принятого на экране
отправки, и игрок должен её увидеть.

```csharp
foreach (RadioOptionOffer offer in e.Options)
{
    Button button = AddButton(entry.FindOption(offer.Id).Name);
    button.Disabled = !offer.IsUnlocked;
    if (!offer.IsUnlocked)
    {
        button.TooltipText = $"Не хватает: {offer.Shortfall}";
    }
}
```

Тот же список можно получить в любой момент: `simulation.GetRadioOptions(incidentId)`.
`ChooseRadioOption` с закрытым вариантом вернёт отказ с той же причиной.

Ключи вариантов идут в порядке файла. Формулировки лежат в `Entry.Options`
записи `mission_event` и достаются по тому же порядку и тем же ключам:

```csharp
ContentEntry entry = Content.Instance.GetEntry(e.MissionEventId);
foreach (ContentOption option in entry.Options)
{
    AddButton(option.Name, () => simulation.ChooseRadioOption(e.IncidentId, option.Id));
}
```

**Ни `canon`, ни `requirement_modifier` в интерфейс не показываются.** Они лежат в тексте
и нужны ядру для расчёта — игрок должен сопоставлять формулировку с абзацами
энциклопедии сам (ДД, раздел 8).

### Блокнот и досье

| Событие | Поля | Что делает интерфейс |
|---|---|---|
| `ScalesChanged` | `Values`, `Delta`, `Reason` | анимация трёх шкал |
| `EmployeeInjured` | `EmployeeId`, `EmployeeName`, `IncidentId` | портрет «травмирован» |
| `EmployeeKilled` | `EmployeeId`, `EmployeeName`, `IncidentId` | портрет «погиб» |
| `EmployeeExperienceGained` | `EmployeeId`, `Amount`, `Total` | полоска опыта |
| `EmployeeLeveledUp` | `EmployeeId`, `NewLevel`, `UnspentSkillPoints` | значок распределения очков |
| `EmployeeStatsChanged` | `EmployeeId`, `Stats`, `UnspentSkillPoints` | обновить характеристики |
| `EmployeeHired` | `EmployeeId`, `EmployeeName`, `Day` | новая карточка |

### Итоги и снаряжение

| Событие | Поля |
|---|---|
| `MissionResolved` | `Outcome` — полный разбор миссии, см. ниже |
| `MissionOutcomeReady` | всё для экрана итога, см. ниже |
| `IncidentClosed` | `IncidentId`, `WasSuccess` |
| `EquipmentConsumed` | `EquipmentId`, `EquipmentName`, `RemainingQuantity` |
| `EquipmentAcquired` | `EquipmentId`, `EquipmentName`, `IsShiftOnly` |
| `EquipmentLost` | `EquipmentId`, `EquipmentName`, `Reason` |

**Порядок при разрешении миссии фиксирован** — на него можно опираться:

```
MissionResolved
  → EmployeeKilled / EmployeeInjured
  → EquipmentConsumed / EquipmentLost
  → ScalesChanged  (и, если порог достигнут, GameOverTriggered)
  → ZoneStateChanged
  → EmployeeExperienceGained / EmployeeLeveledUp
  → CreatureIdentified / CreatureRevealed
  → MissionOutcomeReady          ← экран итога
  → SquadReturned → MissionReportReady → IncidentClosed
```

Если вся группа погибла, `SquadReturned` не приходит: отчёт публикуется сразу,
существо остаётся неопознанным.

### Экран итога миссии

`MissionOutcomeReady` приходит в момент, когда на объекте всё кончилось, — до того,
как группа доедет обратно. Это и есть экран «ВЫПОЛНЕНО / ПРОВАЛЕНО, группа возвращается».

| Поле | Что с ним делать |
|---|---|
| `IsSuccess` | заголовок: выполнено или провалено |
| `SummaryTextId` | запись типа `report` — отдать `ContentTextBox.Open(id)` |
| `CreatureId` | с кем столкнулись; пусто, если никто не вернулся |
| `ReturningEmployeeIds` | кто едет обратно |
| `InjuredEmployeeIds`, `KilledEmployeeIds` | пометки на карточках |
| `ReturnSeconds` | сколько ехать; `0` — возвращаться некому |
| `SquadWiped` | группа погибла целиком |
| `Reason` | `MissionResolutionReason` — почему так вышло |

```csharp
private void OnOutcome(MissionOutcomeReady e)
{
    _outcomeScreen.SetTitle(e.IsSuccess ? "ВЫПОЛНЕНО" : "ПРОВАЛЕНО");
    _outcomeScreen.Body.Open(e.SummaryTextId);   // текст берётся по id
    _outcomeScreen.ShowReturning(e.ReturningEmployeeIds, e.ReturnSeconds);

    // Необязательно: остановить мир, пока игрок читает.
    _kontur.Simulation.OpenMissionOutcome(e.IncidentId);
}

private void OnOutcomeClosed(string incidentId)
{
    _kontur.Simulation.CloseMissionOutcome(incidentId);   // без этого мир так и стоит
}
```

Три вещи, которые стоит знать заранее.

**Текст тот же, что потом ляжет в архив.** `SummaryTextId` и `ReportId` в
`MissionReportReady` — одна и та же запись. Писать две версии одного исхода автору
не нужно, а игрок на компьютере перечитывает ровно то, что видел на экране.

**Пропущенный звонок и протухшая метка сюда не попадают.** Никто никуда не ездил,
докладывать нечего и возвращаться некому. Для них есть `CallMissed` и `MapMarkerExpired`.

**Пара `Open`/`Close` — по желанию.** Если итог показывается плашкой, которая не мешает
играть, эти команды можно вообще не звать. Но если позвали `Open`, то `Close` обязателен:
незакрытый экран держит мир так же, как незакрытый экран отправки.

### Найм между сменами

`HiringOpened` приходит сразу за `ShiftEnded`.

| Поле | Смысл |
|---|---|
| `NextDay` | день, на который набираем |
| `StaffLimit`, `LivingStaff`, `FreeSlots` | сколько мест и сколько занято |
| `CandidateIds` | кандидаты в том же порядке, что вернёт `GetHireCandidates(NextDay)` |

Сигнала **не будет**, если брать некого: партия проиграна, мест в штате нет или
список кандидатов пуст. Открывать меню найма в этих случаях не нужно — пустой список
игрок читает как поломку, а не как «мест нет».

Набор кандидатов зафиксирован на день. `GetHireCandidates(day)` можно звать хоть каждый
кадр: пока день не сменился, вернутся те же люди в том же порядке.

---

## 6. Снимки состояния

| Метод | Возвращает | Когда звать |
|---|---|---|
| `GetStatus()` | `ShiftStatusView` | шапка, отладка |
| `GetRoster()` | `EmployeeView[]` | открытие досье, экран отправки |
| `GetActiveIncidents()` | `IncidentView[]` | перерисовка карты, индикаторы |
| `GetZones()` | `ZoneView[]` | построение карты |
| `GetAvailableEquipment()` | `EquipmentSlotView[]` | экран отправки |
| `GetEncyclopedia()` | `EncyclopediaEntryView[]` | раздел энциклопедии |
| `GetHireCandidates(day)` | `HireCandidateView[]` | экран найма между сменами |
| `GetStartingChoice()` | `HireCandidateView[]` | экран стартового выбора |
| `NeedsStartingChoice` | `bool` | показывать ли экран стартового выбора |
| `GetReports()` | `MissionReport[]` | список отчётов |
| `IsTimeFrozen` | `bool` | открыт экран, мир стоит |
| `IsFlagSet(flag)` | `bool` | условные абзацы, ветвление контента |
| `GetFlags()` | `string[]` | отладка, сохранение |
| `IsPropertyRevealed(creatureId, propertyId)` | `bool` | показывать абзац или заглушку |

`IsFlagSet` и `IsPropertyRevealed` — два вопроса, по которым `ContentTextBox` решает,
показывать условный абзац. Обычно их не зовут напрямую: базовый класс делает это сам,
а на `FlagChanged` и `CreatureRevealed` боксы перечитывают себя.

Состав записей:

```csharp
EmployeeView(Id, Name, RankTitle, Level, Stats, Experience, ExperienceToNextLevel,
             UnspentSkillPoints, Status, IsInjured, CurrentIncidentId, AbilityIds, PortraitId)

IncidentView(Id, MissionId, CallId, ZoneId, MissionEventId, Tier, ConsequenceCap,
             Phase, RemainingSeconds, Requirements, SquadEmployeeIds, EquipmentIds)

ZoneView(Id, Name, State, MapX, MapY)
EquipmentSlotView(Id, Name, Description, Kind, Quantity, IsShiftOnly)
EncyclopediaEntryView(CreatureId, IllustrationId, RevealedPropertyIds, TotalProperties)
HireCandidateView(Id, Name, RankTitle, Level, Stats)
DispatchEstimateView(Requirements, SquadStats, Coverage, SuccessChance, IsAutoSuccess)

ShiftStatusView(Day, IsShiftActive, ShiftTime, IsCallWindowClosed, OpenIncidents,
                PendingCalls, StaffLimit, Scales, IsGameOver, GameOverReason)
```

`GetActiveIncidents()` возвращает только незакрытые вызовы. `MapX`/`MapY` — нормализованные
`0..1` координаты района на карте.

`AbilityIds` — именно id, а не названия: текст перков лежит в текстовом движке
(`content/raw/ru/UI/perks`), и достаёт его слой Godot — `Content.Instance.GetEntry(id).Name`
плюс `Content.Fill(...)` для чисел в описании. Ядро на GodotSharp не ссылается и текста
не носит.

---

## 7. Типы и перечисления

**`StatBlock`** — пять характеристик, неизменяемая структура.

```csharp
int strength = stats[StatKind.Strength];
int total    = stats.Total;
StatBlock sum = a + b;                 // поэлементно
string text  = stats.ToString();       // «Сила 5 Интеллект 3» — нулевые опущены
string label = StatKinds.GetDisplayName(StatKind.Combat);  // «Боевая подготовка»
```

`StatKind`: `Strength`, `Perception`, `Endurance`, `Agility`, `Composure`.

**`ScaleValues`** — `Infection`, `Publicity`, `Loyalty`, диапазон `0..100`.
**`ScaleDelta`** — изменение; положительное значение = рост шкалы.

**`IncidentPhase`** — `Scheduled`, `Queued`, `Ringing`, `Briefing`, `MarkerActive`,
`Travelling`, `OnSite`, `RadioPending`, `Returning`, `Closed`.

`Queued` — время звонка пришло, но линия занята. Таймер не идёт, действий игрока не ждёт.

Ждут действия игрока: `Ringing`, `Briefing`, `MarkerActive`, `RadioPending`.
У `Briefing` таймера нет — вызов ждёт кнопку ОК сколько угодно.

**`EmployeeStatus`** — `Available`, `OnMission`, `Dead`. Погибшие остаются в списке
со статусом `Dead`, не удаляются.

**`EquipmentKind`** — `Consumable` (тратится всегда), `Standard` (возвращается после успеха),
`Story` (теряется только при гибели всей группы).

**`ZoneState`** — `Normal`, `Infected`, `Quarantine`, `Cleared`.

**`MissionTier`** — `Story` (сюжетный вызов: радио, полные ставки) и `Filler`
(фон смены: травмы есть, гибели нет, радио не бывает).

**`StatMatchRating`** — `Exceeds` (превышение на 2+, зелёный), `Meets` (ровно дотянул,
жёлтый), `Below` (недобор, красный). Все зелёные — успех без броска.

**`StatMatch`** — `Stat`, `Required`, `Best`, `IsPrimary`, `Rating`, `Score`, `Margin`,
`Shortfall`. Одна строка экрана отправки.

**`MissionEventQuality`** — `Good`, `Neutral`, `Bad`. Тип диалога: задаёт умолчания
надбавки и рисков и проверяется на сборке. **В интерфейс не передаётся** — подсказок
о правильности быть не должно (ДД, раздел 8).

**`ConsequenceCap`** — `None` (ни травм, ни гибели), `Injury` (гибель исключена),
`Death` (полные ставки). Потолок обрезает шансы **последним**, уже после множителей
варианта и снаряжения. В `IncidentView` он приходит вместе с уровнем — по нему интерфейс
может показать игроку, чем тот рискует, ещё на экране отправки.

**`GameOverReason`** — `InfectionMaxed`, `PublicityMaxed`, `LoyaltyDepleted`.

**`MissionOutcome`** (в событии `MissionResolved`):

```
IncidentId, MissionId, ZoneId, CreatureId
Kind                      Success | Failure
Reason                    StatsCovered | DiceSuccess | DiceFailure | CallMissed | MarkerExpired
EffectiveRequirements     требования с учётом всех множителей
SquadStats                сумма группы
Coverage, SuccessChance   0..1
Roll                      бросок; −1, если броска не было
EmployeeIds, InjuredEmployeeIds, KilledEmployeeIds, EquipmentIds
ScaleDelta, RadioWasTriggered, RadioWasMissed, ChosenRadioOptionId, SquadWiped
```

**`MissionReport`** — `IncidentId`, `MissionId`, `ReportId`, `CreatureId`, `ChosenOptionId`,
`IsSuccess`, `RevealedPropertyIds`. `ReportId` пуст, если автор ещё не написал текст
под такую комбинацию «вариант × исход»; `CreatureId` пуст, если никто не вернулся.
`IsSuccess`, `RevealedPropertyIds`. `CreatureId` пуст, если никто не вернулся.

---

## 8. Рецепты

### Телефон: индикатор обратного отсчёта

```csharp
private void OnIncidentCreated(IncidentCreated e)
{
    _incidentId = e.IncidentId;
    _hasTimer = e.RingSeconds > 0.0;   // ноль = обучающая смена, отсчёта нет
    StartRinging();
}

public override void _Process(double delta)
{
    if (!_hasTimer) return;

    foreach (IncidentView incident in _kontur.Simulation.GetActiveIncidents())
    {
        if (incident.Id == _incidentId && incident.Phase == IncidentPhase.Ringing)
        {
            _dial.Value = incident.RemainingSeconds / 15.0;
            return;
        }
    }
}
```

### Карта: клик по метке открывает экран отправки

```csharp
private void OnMarkerClicked(string incidentId)
{
    // Ядро само опубликует DispatchScreenRequested — компьютер поймает и откроется.
    _kontur.Simulation.OpenDispatchScreen(incidentId);
}
```

### Компьютер: экран отправки

```csharp
private void RefreshEstimate()
{
    DispatchEstimateView estimate = _kontur.Simulation.EstimateDispatch(
        _incidentId, _pickedEmployees, _pickedEquipment);

    _requirementsLabel.Text = estimate.Requirements.ToString();
    _squadLabel.Text = estimate.SquadStats.ToString();
    _verdictLabel.Text = estimate.IsAutoSuccess ? "ТРЕБОВАНИЯ ПОКРЫТЫ" : "РИСК";
}

private void OnSendPressed()
{
    CommandResult result = _kontur.Simulation.DispatchSquad(
        _incidentId, _pickedEmployees, _pickedEquipment);

    if (!result.IsSuccess)
    {
        _errorLabel.Text = result.Error;   // готовый текст для оператора
    }
}
```

Живой пример целиком — `scripts/debug/KonturDebugOverlay.cs`, метод `RefreshDispatch`.

### Радио: три варианта

```csharp
private void OnRadioTriggered(RadioTriggered e)
{
    _incidentId = e.IncidentId;

    // Вводная — куски записи; варианты — её Options. И то и другое по одному id.
    _situationBox.Open(e.MissionEventId);

    ContentEntry entry = Content.Instance.GetEntry(e.MissionEventId);
    for (int i = 0; i < entry.Options.Count; i++)
    {
        string optionId = entry.Options[i].Id;
        _buttons[i].Text = entry.Options[i].Name;
        _buttons[i].Pressed += () => _kontur.Simulation.ChooseRadioOption(_incidentId, optionId);
    }
}
```

### Блокнот: анимация шкал

```csharp
private void OnScalesChanged(ScalesChanged e)
{
    _infection.AnimateTo(e.Values.Infection);
    _publicity.AnimateTo(e.Values.Publicity);
    _loyalty.AnimateTo(e.Values.Loyalty);
}
```

### Менеджер сцен: ролики и финалы

```csharp
private void OnShiftEnded(ShiftEnded e)  => PlayCutscene(e.OutroCutsceneId);
private void OnGameOver(GameOverTriggered e) => ShowEnding(e.Reason);
```

---

## 9. Обучающая смена: чего ждать интерфейсу

День 1 в поставляемом контенте настроен как обучающий. Ядро при этом ведёт себя иначе,
и интерфейс должен быть к этому готов. Всё задаётся в `data/config.json`, кода это не
касается — любой день можно сделать обучающим или наоборот.

**Таймеры игрока отключены.** В событиях `IncidentCreated`, `MapMarkerSpawned` и
`RadioTriggered` длительность приходит равной `0`. Это признак «без ограничения»:

```csharp
_hasCountdown = e.RingSeconds > 0.0;
```

Если рисовать индикатор по нулю, он мгновенно окажется в конце шкалы и будет врать.
Правильно — не показывать отсчёт вовсе. `IncidentView.RemainingSeconds` в этих фазах
тоже равен нулю.

Таймеры дороги, работы на объекте и возвращения продолжают идти всегда — иначе вызов
не завершился бы.

**Вызовы идут по одному.** Первые несколько вызовов смены (`sequentialCallCount`)
не приходят, пока не закрыт предыдущий. Наложения в начале обучения не будет, в конце —
будет: последние вызовы идут обычным расписанием, и это специально, чтобы показать
игроку деление штата.

**Порядок вызовов фиксирован** (`missionOrder`), случайного подбора нет.
Сценарная смена завершается сразу после закрытия последнего вызова, не дожидаясь
конца пятиминутного окна — `CallWindowClosed` в ней может вообще не прийти.

Подробности полей — `kontur-core/docs/CONTENT_SCHEMA.md`, раздел «Сценарные и обучающие смены».

## 10. Соседство с `scripts/gameplay`

В проекте есть второй слой — автозагрузки `EventBus` и `GameSession` из
`scripts/gameplay/`. Это отдельная работа, к ядру она не подключена.

Пересечение существенное: день, состояние смены и отсчёт времени считаются **в двух
местах независимо**. `GameSession.CurrentDay` и `KonturSimulation.Day` — разные числа,
и они разойдутся, как только одна из сторон начнёт менять своё состояние.

Пока предметы стола подключаются к ядру, берите день, фазы и время из
`KonturSimulation.GetStatus()`, а не из `GameSession`. Свести два слоя в один источник
истины стоит отдельной задачей и вместе с автором `scripts/gameplay`.

## 11. Грабли

**Таймер продолжает идти, пока открыт экран отправки.** По ДД на отправку 30 секунд
с момента появления метки. Если интерфейс «останавливает мир» на экране выбора состава,
метка всё равно истечёт — это задумано.

**Пропущенное радио штрафует дважды.** Вариант не выбран, значит снижение требований
не применяется, и сверх того шанс режется вдвое.

**Смена не заканчивается по таймеру.** Через 5 минут закрывается только приём новых
вызовов (`CallWindowClosed`). `ShiftEnded` придёт, когда закроется последний вызов.

**Вызов в фазе `Briefing` висит вечно,** пока не пришёл `ConfirmBriefing`. Если экран
задания можно закрыть «крестиком», не забудьте всё равно вызвать команду.

**`IncidentId` ≠ `MissionId`.** Первый уникален для вызова в смене (`INC-01-03`),
второй — идентификатор авторского контента (`m_d1_05`). Команды принимают `IncidentId`.

**Смерть приходит до опыта.** В цепочке событий `EmployeeKilled` раньше
`EmployeeExperienceGained`, и погибшим опыт не начисляется.

**Незакрытый экран итога держит мир.** Как и экран отправки: позвали
`OpenMissionOutcome` — обязаны позвать `CloseMissionOutcome`.

**После `Load` время стоит,** пока не вызван `ResumeAfterLoad()`. Забыли — игра
выглядит зависшей, хотя ядро просто ждёт.

**Кандидаты фиксируются на день.** `GetHireCandidates(day)` не перегенерирует список
при каждом вызове — иначе кандидат прыгал бы под курсором игрока. Набор меняется
при переходе на новый день либо принудительно через `RefreshHireCandidates(day)`.

---

## 12. Очередь звонков

**Телефон один.** Одновременно звонить может только один вызов — иначе реплики двух
звонящих накладывались бы друг на друга и разобрать было бы нельзя ничего.

Линия считается занятой, пока вызов в фазе `Ringing` **или** `Briefing`: с точки зрения
игрока это один разговор — снял трубку, дослушал, закрыл бланк.

```
   время вызова пришло  →  Queued          (ждёт, таймер не идёт)
   линия освободилась   →  Ringing         (IncidentCreated, пошли 15 секунд)
   не взяли трубку      →  CallMissed      → линия свободна через паузу
   закрыли бланк        →  MapMarkerSpawned → линия свободна через паузу
```

Очередь **строго по времени поступления**: пришедший раньше зазвонит раньше, даже если
он менее срочный. Отсчёт пятнадцати секунд начинается в момент, когда телефон реально
зазвонил, а не когда вызов попал в очередь — иначе игрок терял бы вызовы, которых
ещё не слышал.

Между звонками есть пауза (`callQueueGapSeconds`, по умолчанию 2 секунды): без неё
следующий вызов начинался бы встык и сливался с предыдущим в один поток.

**Что видит интерфейс.** `IncidentQueued` приходит только если вызов действительно
пришлось задержать, и несёт позицию в очереди — по нему рисуется индикатор «на линии
ждут». `IncidentCreated` по-прежнему означает ровно одно: телефон зазвонил **сейчас**.
Сколько сейчас в очереди, есть и в снимке: `GetStatus().QueuedCalls`.

**Грабли.** Пока открыт бланк задания, линия занята, а очередь стоит. У фазы `Briefing`
таймера нет — если интерфейс позволит закрыть бланк «крестиком», не вызвав
`ConfirmBriefing`, вызов застрянет в `Briefing` навсегда и остальные звонки не поступят.
Закрывать бланк нужно только командой.

## 13. Остановка глобального времени

Два экрана останавливают мир целиком: экран отправки группы и экран вариантов по радио.
Пока открыт хотя бы один из них, `Tick` не двигает **ничего** — ни таймеры вызовов,
ни дорогу группы к месту, ни часы смены, а значит и новые звонки не поступают.

```
       команда игрока            что происходит со временем
  ────────────────────────────────────────────────────────────
  OpenDispatchScreen(id)   →   стоп     (мир ждёт)
  DispatchSquad(...)       →   пуск     (группа выехала)
  CloseDispatchScreen(id)  →   пуск     (передумал, метка висит дальше)

  AnswerRadio(id)          →   стоп     (мир ждёт)
  ChooseRadioOption(...)   →   пуск     (решение принято)
  CloseRadio(id)           →   пуск     (отложил, отсчёт продолжается)
```

**Таймеры при этом не сбрасываются и не отменяются.** Они давят на реакцию, а не
на качество решения: 15 секунд заметить звонок, 30 — нажать на метку, 20 — взять радио.
Как только экран открыт, у игрока есть сколько угодно времени сравнить характеристики
и свериться с энциклопедией. Закрыл экран не решив — отсчёт продолжается **с того же
места**, а не начинается заново.

**Чтение звонка мир не останавливает.** Пока игрок листает реплики старика в трубке,
город живёт: чужая метка может истечь. Это сознательное исключение — единственное место,
где текст и время конкурируют.

Состояние доступно и как флаг, и как событие:

```csharp
if (simulation.IsTimeFrozen) { /* не анимировать движение по карте */ }

simulation.Events.Subscribe<TimeFreezeChanged>(e =>
{
    _countdownRing.Visible = !e.IsFrozen;
});
```

Флаг есть и в `GetStatus().IsTimeFrozen`.

**Держателей может быть несколько.** Экраны считаются множеством: если открыт экран
отправки по одному вызову и радио по другому, время пойдёт только когда закрыт последний.
При завершении и обрыве смены все удержания снимаются принудительно — зависнуть
на закрытом вызове невозможно.

**Грабли для интерфейса.** Открыли экран — обязаны закрыть. Если сцена показала экран
отправки и уничтожилась, не позвав `CloseDispatchScreen`, мир останется стоять, и это
будет выглядеть как зависшая игра. Снимайте удержание в `_ExitTree()` так же, как подписки.

## 14. Связь с текстовым движком

Ядро и тексты сходятся ровно в трёх местах.

**Ссылки в геймплейных данных.** `data/missions.json` хранит `callId`, `missionEventId`
и таблицу `reports` (ключ — id варианта решения, пустая строка — исход без вмешательства).
`data/config.json` хранит `shiftNoteId` и `outroCutsceneId` по дням. Все они — id записей
из `content/raw`.

**Сверка при загрузке.** Ядро получает порт `ITextCatalog` и падает на старте, если id
не найден:

```
Миссия 'm_pensioner_door': нет записи report с id 'report_pensioner_door_pass_by_succes'
Вмешательство 'mission_event_black_mold': вариант 'reagant' есть в данных, но не в тексте
Существо 'creature_mimic': в статье нет блока %% reveal: property_mimic_TYPO %%
```

Реализации: `GodotTextCatalog` (поверх автозагрузки `Content`) в игре и `JsonTextCatalog`
(читает тот же собранный JSON) в консольном прогоне. Без каталога сверка пропускается —
ядро остаётся запускаемым без движка.

**Числа вариантов приходят из текста.** Единственное исключение из правила «в тексте нет
чисел»: у варианта решения `requirement_modifier` и `canon` лежат в `.md` рядом
с формулировкой, потому что автор правит их вместе. Ядро читает их через каталог,
остальное — риски, шкалы, карантин — берёт из `data/mission_events.json`.
Наборы ключей вариантов в тексте и в данных обязаны совпадать.

`requirement_modifier` — надбавка: **+N к каждой требуемой характеристике** миссии.
Требование `[Сила 8, Интеллект 4]` с модификатором 2 превращается в `[Сила 10, Интеллект 6]`;
нулевые характеристики не трогаются.

## 15. Сохранение партии

Снимок снимается в любой момент, **в том числе посреди смены**: фазы вызовов,
недотикавшие таймеры и состояние генератора случайных чисел попадают в файл.

```csharp
string json = simulation.Save("день 3, 04:12");   // побочных эффектов нет
CommandResult result = simulation.Load(json);
if (result.IsSuccess)
{
    RebuildDesk();                    // перерисовать всё по Get*-снимкам
    simulation.ResumeAfterLoad();     // и только теперь пустить время
}
```

В Godot то же самое через мост — он сам разложит файлы по `user://saves/`:

```csharp
_kontur.SaveToSlot("slot1", "день 3");
if (_kontur.LoadFromSlot("slot1"))
{
    RebuildDesk();
    _kontur.Simulation.ResumeAfterLoad();
}
```

### Четыре правила

**После загрузки мир остановлен.** `Load` замораживает время и ждёт `ResumeAfterLoad()`.
Сделано нарочно: интерфейсу нужно время перерисовать стол, а игроку — сообразить,
где он. Иначе таймер звонка, на котором сохранились, тикал бы под ещё не нарисованным
телефоном. Забудете вызвать — игра встанет намертво.

**Перерисовываться надо по снимкам, а не по событиям.** Загрузка публикует ровно один
сигнал — `GameLoaded`. Потока событий, который привёл партию в это состояние, не было:
было чтение файла. Всё, что нужно нарисовать, лежит в `GetStatus()`, `GetRoster()`,
`GetActiveIncidents()`, `GetZones()`, `GetEncyclopedia()`, `GetReports()`.

**Модальные экраны считаются закрытыми.** Отправка, радио, итог — после загрузки их нет.
Что открыть заново, видно по фазам в `GetActiveIncidents()`: `MarkerActive` — метка висит,
`RadioPending` — радио трещит.

**Отказ ничего не портит.** Если файл битый, версия чужая или в сохранении есть вызов
по миссии, которой больше нет в контенте, `Load` вернёт `Fail` с причиной, а партия
останется ровно такой, какой была. Проверка идёт целиком до того, как применено хоть
что-нибудь: половина загруженной партии хуже, чем отказ загружать.

### Что в файле, а чего в нём нет

Внутри — только то, что игрок наиграл: штат, шкалы, флаги, энциклопедия, склад, отчёты,
кандидаты, состояние смены и состояние генератора случайных чисел.

Ничего из контента внутрь **не попадает**: ни требований миссий, ни характеристик
снаряжения, ни текстов. Это не экономия места, а свойство: исправленная опечатка
и подкрученный баланс сами доезжают до уже начатых партий.

Отсюда же следует и ограничение — переименовали миссию, и старые сохранения с ней
в работе перестанут грузиться. Сообщение об отказе назовёт пропавшие id.

`Version` в файле — версия формата. Меняете раскладку полей несовместимо — поднимайте
`SaveData.CurrentVersion`, тогда старые файлы отклонятся с внятным сообщением,
а не упадут на разборе.

Мелочь, о которую можно споткнуться: `Simulation.Seed` после загрузки остаётся тем,
с которым объект создавали, — восстанавливается не сид, а состояние генератора.
На воспроизводимость это не влияет, но в отладочном выводе числа будут разные.

---

## 16. Чего ядро не делает

- Не рисует, не проигрывает звук, не знает о сценах.
- Не хранит координаты точки вызова. Ядро называет **дом** (`BuildingId`) и **район**
  (`ZoneId`), а где этот дом на экране — знает только карта. Координаты района как
  запасной вариант лежат в `ZoneView.MapX/MapY`.
- Не отдаёт прогресс движения группы по маршруту — считайте по `TravelSeconds`.
- Не выдаёт сюжетное снаряжение: правила расхода описаны, механизма получения нет.
- Не хранит и не генерирует тексты. В геймплейных данных лежат только ссылки по id,
  сама проза живёт в `content/raw` и собирается конвертером; схемы — в
  `kontur-core/docs/CONTENT_SCHEMA.md` и `content/raw/_system/Syntax/README.md`.

---

## Куда смотреть дальше

| Вопрос | Файл |
|---|---|
| Почему архитектура такая | `kontur-core/docs/ARCHITECTURE.md` |
| Формат JSON-контента | `kontur-core/docs/CONTENT_SCHEMA.md` |
| Как подключено к Godot | `kontur-core/docs/INTEGRATION.md` |
| Рабочий пример всех вызовов API | `scripts/debug/KonturDebugOverlay.cs` |
| Прогон без движка | `kontur-core/README.md` |
| Настройка обучающих смен | `kontur-core/docs/CONTENT_SCHEMA.md` |
| Как выводить текст по id | `docs/ТЕКСТОВЫЕ-БОКСЫ.md` |
| Схема .md для авторов | `content/raw/_system/Syntax/README.md` |
| Как придумывать вызовы | `docs/КАК-ДЕЛАТЬ-МИССИИ.md` |
| Меню, ролики, экраны найма | `docs/ЭКРАНЫ-И-ПОТОК.md` |
