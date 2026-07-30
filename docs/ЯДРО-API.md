# Ядро К.О.Н.Т.У.Р. — руководство для интерфейса

Документ для того, кто прикручивает предметы на столе к симуляционному ядру.
Всё, что здесь описано, находится в сборке `Kontur.Core` и работает без Godot.

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
        StartRinging(e.CallerName, e.RingSeconds);
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

## 3. Пять правил, которые экономят время

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

---

## 4. Команды

Все возвращают `CommandResult` — `IsSuccess` и `Error` (человекочитаемая причина отказа).
Ядро никогда не бросает исключения на действия игрока.

| Команда | Когда допустима | Порождает |
|---|---|---|
| `StartShift(day)` | смена не идёт, партия не проиграна | `ShiftStarted` |
| `AnswerCall(id)` | фаза `Ringing` | `CallAnswered` |
| `ConfirmBriefing(id)` | фаза `Briefing` | `MapMarkerSpawned` |
| `OpenDispatchScreen(id)` | фаза `MarkerActive` | `DispatchScreenRequested` |
| `DispatchSquad(id, employeeIds, equipmentIds)` | фаза `MarkerActive` | `SquadDispatched` |
| `ChooseRadioOption(id, optionId)` | фаза `RadioPending` | `RadioOptionChosen` |
| `SpendSkillPoint(employeeId, stat)` | есть нераспределённые очки | `EmployeeStatsChanged` |
| `HireEmployee(candidateId, day)` | смена не идёт, лимит штата не исчерпан | `EmployeeHired` |
| `ForceEndShift()` | всегда | `ShiftEnded` |
| `ResetToNewGame()` | всегда | — (см. ниже) |
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
// estimate.Requirements  — требования с учётом дня, штриховки зоны и выбранного радио-варианта
// estimate.SquadStats    — сумма группы со снаряжением и сработавшими перками
// estimate.Coverage      — 0..1
// estimate.SuccessChance — 0..1
// estimate.IsAutoSuccess — требования покрыты, броска не будет
```

Побочных эффектов нет: кубик не бросается, склад не трогается. Можно дёргать на каждое
переключение галочки. Считает тот же код, что и настоящий резолв, поэтому экран отправки
физически не может разойтись с итогом миссии.

Показывать игроку требования и сумму группы — нормально. Показывать точный процент —
на усмотрение дизайна: по ДД (раздел 8) прямых подсказок в интерфейсе быть не должно.

---

## 5. События

### Смена

| Событие | Поля | Кто реагирует |
|---|---|---|
| `ShiftStarted` | `Day`, `StaffLimit`, `ShiftNoteTitle`, `ShiftNoteText` | записка сменщика, номер дня |
| `CallWindowClosed` | `Day`, `OpenIncidents` | необязательно: подсказка «новых вызовов не будет» |
| `ShiftEnded` | `Day`, `OutroCutsceneId`, `Summary` | менеджер сцен → пререндеренный ролик |
| `GameOverTriggered` | `Reason`, `Values`, `Day` | финальный экран под причину |

`Summary` — `ShiftSummary(TotalIncidents, Successes, Failures, MissedCalls, ExpiredMarkers, Injuries, Deaths)`.

### Телефон

| Событие | Поля | Что делает интерфейс |
|---|---|---|
| `IncidentCreated` | `IncidentId`, `MissionId`, `ZoneId`, `CallerName`, `RingSeconds` | начать звонок, запустить индикатор |
| `CallAnswered` | `IncidentId`, `MissionId`, `Title`, `BriefingText` | показать бланк задания |
| `CallMissed` | `IncidentId`, `MissionId` | оборвать звонок |

### Карта

| Событие | Поля | Что делает интерфейс |
|---|---|---|
| `MapMarkerSpawned` | `IncidentId`, `ZoneId`, `LifetimeSeconds` | поставить метку, индикатор |
| `MapMarkerExpired` | `IncidentId`, `ZoneId` | снять метку |
| `SquadDispatched` | `IncidentId`, `EmployeeIds`, `EquipmentIds`, `TravelSeconds` | пунктирная линия к точке |
| `SquadArrived` | `IncidentId`, `ZoneId` | линия дошла |
| `SquadReturned` | `IncidentId`, `EmployeeIds` | линия обратно, убрать метку |
| `ZoneStateChanged` | `ZoneId`, `OldState`, `NewState`, `Reason` | переключить слой штриховки |

Прогресса маршрута ядро не отдаёт: берите `TravelSeconds` из события и интерполируйте
у себя, либо `IncidentView.RemainingSeconds` в фазе `Travelling`.

### Компьютер

| Событие | Поля | Что делает интерфейс |
|---|---|---|
| `DispatchScreenRequested` | `IncidentId`, `MissionId` | открыть экран отправки |
| `MissionReportReady` | `Report` | добавить отчёт |
| `CreatureIdentified` | `CreatureId` | новая карточка в энциклопедии |
| `CreatureRevealed` | `CreatureId`, `PropertyId` | открыть абзац под это свойство |

В обоих событиях **прозы нет намеренно**: имя существа и абзацы статьи лежат в
текстовом движке под тем же id. Разворачивает их интерфейс — см.
[ТЕКСТОВЫЕ-БОКСЫ.md](ТЕКСТОВЫЕ-БОКСЫ.md).

### Радиостанция

| Событие | Поля | Что делает интерфейс |
|---|---|---|
| `RadioTriggered` | `IncidentId`, `SituationText`, `Options`, `ResponseSeconds` | треск, экран вариантов |
| `RadioMissed` | `IncidentId` | погасить экран |
| `RadioOptionChosen` | `IncidentId`, `OptionId`, `OptionText` | подтверждение выбора |

`Options` — список `RadioOptionView(Id, Text)`. **Больше в нём ничего нет намеренно:**
качество варианта и множители в интерфейс не передаются, игрок должен сопоставить текст
с абзацами энциклопедии сам.

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
  → SquadReturned → MissionReportReady → IncidentClosed
```

Если вся группа погибла, `SquadReturned` не приходит: отчёт публикуется сразу,
существо остаётся неопознанным.

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
| `GetReports()` | `MissionReport[]` | список отчётов |

Состав записей:

```csharp
EmployeeView(Id, Name, RankTitle, Level, Stats, Experience, ExperienceToNextLevel,
             UnspentSkillPoints, Status, IsInjured, CurrentIncidentId, AbilityIds, PortraitId)

IncidentView(Id, MissionId, Title, ZoneId, CallerName, Phase, RemainingSeconds,
             Requirements, SquadEmployeeIds, EquipmentIds)

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
(`content/raw/UI/perks`), и достаёт его слой Godot — `Content.Instance.GetEntry(id).Name`
плюс `Content.Fill(...)` для чисел в описании. Ядро на GodotSharp не ссылается и текста
не носит.

---

## 7. Типы и перечисления

**`StatBlock`** — пять характеристик, неизменяемая структура.

```csharp
int strength = stats[StatKind.Strength];
int total    = stats.Total;
StatBlock sum = a + b;                 // поэлементно
string text  = stats.ToString();       // «Сила 5 Восприятие 3» — нулевые опущены
string label = StatKinds.GetDisplayName(StatKind.Composure);  // «Хладнокровие»
```

`StatKind`: `Strength`, `Perception`, `Endurance`, `Agility`, `Composure`.

**`ScaleValues`** — `Infection`, `Publicity`, `Loyalty`, диапазон `0..100`.
**`ScaleDelta`** — изменение; положительное значение = рост шкалы.

**`IncidentPhase`** — `Scheduled`, `Ringing`, `Briefing`, `MarkerActive`, `Travelling`,
`OnSite`, `RadioPending`, `Returning`, `Closed`.

Ждут действия игрока: `Ringing`, `Briefing`, `MarkerActive`, `RadioPending`.
У `Briefing` таймера нет — вызов ждёт кнопку ОК сколько угодно.

**`EmployeeStatus`** — `Available`, `OnMission`, `Dead`. Погибшие остаются в списке
со статусом `Dead`, не удаляются.

**`EquipmentKind`** — `Consumable` (тратится всегда), `Standard` (возвращается после успеха),
`Story` (теряется только при гибели всей группы).

**`ZoneState`** — `Normal`, `Infected`, `Quarantine`, `Cleared`.

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

**`MissionReport`** — `IncidentId`, `MissionId`, `Title`, `Text`, `CreatureId`,
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
    _situationLabel.Text = e.SituationText;

    for (int i = 0; i < e.Options.Count; i++)
    {
        string optionId = e.Options[i].Id;
        _buttons[i].Text = e.Options[i].Text;
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

---

## 12. Чего ядро не делает

- Не рисует, не проигрывает звук, не знает о сценах.
- Не сохраняет и не загружает партию — всё в памяти, закрытие игры обнуляет прогресс.
- Не хранит координаты конкретной точки вызова: у миссии есть только район (`ZoneId`),
  координаты берутся у зоны (`ZoneView.MapX/MapY`).
- Не отдаёт прогресс движения группы по маршруту — считайте по `TravelSeconds`.
- Не выдаёт сюжетное снаряжение: правила расхода описаны, механизма получения нет.
- Не генерирует тексты. Всё приходит из `res://data/*.json`; схема — в
  `kontur-core/docs/CONTENT_SCHEMA.md`.

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
