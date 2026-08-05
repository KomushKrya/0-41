---
id: radio_workshop_gravel
type: radio
mission_id: m_workshop_gravel
status: draft
---

%% dev %%
Выбор ставит флаг: deliver -> flag_gravel_delivered, refuse -> flag_gravel_refused.
%% /dev %%

Бригада у ворот охраняемой территории. Щебень лежит открыто, погрузчик на ходу, сторож в другом конце двора и в эту сторону не смотрит.

Цеховик ждёт на связи.

## Вариант: Вывезти пару ящиков
id: deliver
requires: сила

Взять с краю партии, накрыть брезентом, в журнал не вписывать.

## Вариант: Отказать цеховику
id: refuse

Ничего не грузить, доложить о запросе как есть.
