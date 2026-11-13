#!/usr/bin/env python3
"""Interactive explorer for the DeepCool AK400 DIGITAL display.
Run in your own terminal:  sudo python3 -I ~/ak400-explore.py
Stops ak400-display.service while running and restarts it at the end.
Answers are saved to ~/ak400-explore-results.txt
"""
import os, sys, subprocess, importlib.util, importlib.machinery
loader = importlib.machinery.SourceFileLoader('drv', '/usr/local/bin/ak400-display')
spec = importlib.util.spec_from_loader('drv', loader)
drv = importlib.util.module_from_spec(spec)
loader.exec_module(drv)

OUT = os.path.expanduser('~' + os.environ.get('SUDO_USER', '')) + '/ak400-explore-results.txt'

def frame(icon=19, bar=5, d=(0, 4, 2), alarm=0, tail=0):
    b = [16, icon, bar, d[0], d[1], d[2], alarm] + [tail] * 57
    return bytes(b)

TESTS = [
    ('Эталон: °C, шкала 5, "042"', frame()),
    ('Иконка 0x00 (ничего?)', frame(icon=0x00)),
    ('Иконка 0x10', frame(icon=0x10)),
    ('Иконка 0x20', frame(icon=0x20)),
    ('Иконка 0x40', frame(icon=0x40)),
    ('Иконка 0x80', frame(icon=0x80)),
    ('Иконка 0x01', frame(icon=0x01)),
    ('Иконка 0x02', frame(icon=0x02)),
    ('Иконка 0x04', frame(icon=0x04)),
    ('Иконка 0x08', frame(icon=0x08)),
    ('Иконка 0x7F (все биты)', frame(icon=0x7F)),
    ('Иконка 0xFF', frame(icon=0xFF)),
    ('Иконка 35 (°F?)', frame(icon=35)),
    ('Иконка 76 (%?)', frame(icon=76)),
    ('Цифры 10,11,12', frame(d=(10, 11, 12))),
    ('Цифры 13,14,15', frame(d=(13, 14, 15))),
    ('Цифры 16,17,18', frame(d=(16, 17, 18))),
    ('Цифры 255,255,255', frame(d=(255, 255, 255))),
    ('Цифры 0,0,0 (ведущий ноль виден?)', frame(d=(0, 0, 0))),
    ('Шкала 0', frame(bar=0)),
    ('Шкала 10', frame(bar=10)),
    ('Шкала 11', frame(bar=11)),
    ('Шкала 255', frame(bar=255)),
    ('Тревога = 1', frame(alarm=1)),
    ('Тревога = 2', frame(alarm=2)),
    ('Хвост пакета заполнен 0xFF', frame(tail=0xFF)),
]

if '--digits' in sys.argv:
    TESTS = [('Эталон: °C, шкала 5, "042"', frame())] + [
        (f'Код {c} во всех трёх цифрах — какой символ?', frame(d=(c, c, c))) for c in range(10, 26)
    ] + [('Код 10 слева, затем 4 и 2', frame(d=(10, 4, 2)))]
    OUT = OUT.replace('.txt', '-digits.txt')


def scan():
    """Auto slideshow over icon / bar / alarm codes. Press Enter to mark."""
    import select, time
    speed = 1.2
    plan = [('icon', c, frame(icon=c, bar=5, d=(c // 100 or 10, (c // 10 % 10) if c >= 10 else 10, c % 10)))
            for c in range(256)]
    plan += [('bar', c, frame(bar=c, d=(c // 100 or 10, (c // 10 % 10) if c >= 10 else 10, c % 10)))
             for c in range(11, 41)]
    plan += [('alarm', c, frame(alarm=c, d=(10, 10, c % 10))) for c in range(3, 11)]
    out = OUT.replace('.txt', '-scan.txt')
    subprocess.run(['systemctl', 'stop', 'ak400-display'], check=False)
    marks, fd = [], None
    print('Автоперебор: %d кадров по %.1f с (~%d мин).' % (len(plan), speed, len(plan) * speed // 60 + 1))
    print('Номер кода виден на цифрах (если экран не погас) и здесь.')
    print('Жмите Enter, когда видите что-то НОВОЕ (не TEMP/USAGE/°C/°F/%). Ctrl+C — стоп.\n')
    try:
        for kind, code, pkt in plan:
            for _ in range(3):
                try:
                    if fd is None:
                        fd = drv.open_dev()
                    drv.usb_write(fd, pkt)
                    break
                except (OSError, TypeError):
                    fd = None
                    time.sleep(1.5)
            print(f'\r{kind:5} = {code:3}   ', end='', flush=True)
            if select.select([sys.stdin], [], [], speed)[0]:
                sys.stdin.readline()
                marks.append(f'{kind} {code}')
                print(f'  <- отмечено ({kind} {code}, проверю и соседние)')
    except KeyboardInterrupt:
        pass
    finally:
        with open(out, 'w') as f:
            f.write('\n'.join(marks) + '\n')
        if fd is not None:
            os.close(fd)
        subprocess.run(['systemctl', 'start', 'ak400-display'], check=False)
        print(f'\nОтмечено: {marks or "ничего"}. Сохранено в {out}, служба запущена.')


def push(fd, pkt):
    import time
    for _ in range(3):
        try:
            if fd is None:
                fd = drv.open_dev()
            drv.usb_write(fd, pkt)
            return fd
        except (OSError, TypeError):
            fd = None
            time.sleep(1.5)
    return fd


def glyphs():
    """Find digit-glyph codes that look like letters."""
    import select
    out = OUT.replace('.txt', '-glyphs.txt')
    subprocess.run(['systemctl', 'stop', 'ak400-display'], check=False)
    fd, seen, letters = None, set(), []
    try:
        print('ЭТАП 1: автоперебор кодов 26..255 (по 1 с, ~4 мин).')
        print('Жмите Enter, как только на цифрах появится ЛЮБОЙ символ (не пусто). Ctrl+C — к этапу 2.\n')
        try:
            for c in range(26, 256):
                fd = push(fd, frame(icon=19, bar=0, d=(c, c, c)))
                print(f'\rкод {c:3}   ', end='', flush=True)
                if select.select([sys.stdin], [], [], 1.0)[0]:
                    sys.stdin.readline()
                    seen.update(x for x in (c - 2, c - 1, c) if x >= 26)
                    print(f'  <- отмечено (проверю {max(26, c - 2)}..{c})')
        except KeyboardInterrupt:
            print()
        cand = [12, 13, 14, 15, 16, 19, 20, 21, 22] + sorted(seen)
        print('\nЭТАП 2: пошагово. Напишите, на какую БУКВУ похож символ '
              '(C, P, U, G, H, E, L, r, n, o, t...), или опишите. Enter = ни на что / пусто.\n')
        for c in cand:
            fd = push(fd, frame(icon=19, bar=0, d=(c, c, c)))
            ans = input(f'код {c:3}: ').strip()
            if ans.lower() == 'q':
                break
            if ans:
                letters.append(f'{c} -> {ans}')
    finally:
        with open(out, 'w') as f:
            f.write('\n'.join(letters) + '\n')
        if fd is not None:
            os.close(fd)
        subprocess.run(['systemctl', 'start', 'ak400-display'], check=False)
        print(f'\nСохранено в {out}, служба запущена.')


def main():
    if '--scan' in sys.argv:
        return scan()
    if '--glyphs' in sys.argv:
        return glyphs()
    subprocess.run(['systemctl', 'stop', 'ak400-display'], check=False)
    fd = None
    results = []
    try:
        print('Для каждого теста опишите, что на экране (Enter = как на эталоне, q = выход).\n')
        for i, (name, pkt) in enumerate(TESTS, 1):
            for attempt in range(3):
                try:
                    if fd is None:
                        fd = drv.open_dev()
                    drv.usb_write(fd, pkt)
                    break
                except (OSError, TypeError):
                    fd = None
                    import time; time.sleep(1.5)
            ans = input(f'[{i:2}/{len(TESTS)}] {name}: ').strip()
            if ans.lower() == 'q':
                break
            results.append(f'{i}. {name} -> {ans or "(как эталон)"}')
    finally:
        with open(OUT, 'w') as f:
            f.write('\n'.join(results) + '\n')
        if fd is not None:
            os.close(fd)
        subprocess.run(['systemctl', 'start', 'ak400-display'], check=False)
        print(f'\nСохранено в {OUT}, служба снова запущена.')

if __name__ == '__main__':
    main()
