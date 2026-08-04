from pathlib import Path
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import landscape, A4
from reportlab.lib.colors import HexColor
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfbase.pdfmetrics import stringWidth


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output" / "pdf" / "HonestFlow_one_page_offer.pdf"
OUT.parent.mkdir(parents=True, exist_ok=True)
W, H = landscape(A4)

NAVY = HexColor("#12233F")
BLUE = HexColor("#246BFE")
CYAN = HexColor("#18AEC4")
GREEN = HexColor("#198754")
AMBER = HexColor("#C77D00")
INK = HexColor("#172033")
MUTED = HexColor("#66758C")
LINE = HexColor("#DCE3EC")
PAPER = HexColor("#F4F7FB")
WHITE = HexColor("#FFFFFF")


def fonts():
    regular = "C:/Windows/Fonts/segoeui.ttf"
    semibold = "C:/Windows/Fonts/seguisb.ttf"
    bold = "C:/Windows/Fonts/segoeuib.ttf"
    pdfmetrics.registerFont(TTFont("R", regular))
    pdfmetrics.registerFont(TTFont("S", semibold))
    pdfmetrics.registerFont(TTFont("B", bold))


fonts()


def wrap(text, font, size, width):
    lines, current = [], ""
    for word in text.split():
        candidate = word if not current else current + " " + word
        if stringWidth(candidate, font, size) <= width:
            current = candidate
        else:
            if current:
                lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines


def para(c, text, x, y, width, size=9, leading=12, color=INK, font="R"):
    c.setFont(font, size)
    c.setFillColor(color)
    for line in wrap(text, font, size, width):
        c.drawString(x, y, line)
        y -= leading
    return y


def card(c, x, y, w, h, fill=WHITE, stroke=LINE, radius=10):
    c.setFillColor(fill)
    c.setStrokeColor(stroke)
    c.roundRect(x, y, w, h, radius, fill=1, stroke=1)


def pill(c, x, y, text, fill):
    tw = stringWidth(text, "S", 7.5) + 18
    c.setFillColor(fill)
    c.roundRect(x, y, tw, 18, 9, fill=1, stroke=0)
    c.setFillColor(WHITE)
    c.setFont("S", 7.5)
    c.drawString(x + 9, y + 5.5, text)


def bullet(c, x, y, text, width, dot=BLUE, fg=INK, size=8.2):
    c.setFillColor(dot)
    c.circle(x + 3, y + 2.5, 2.5, fill=1, stroke=0)
    return para(c, text, x + 13, y + 5, width - 13, size, 10.5, fg, "R") - 2


def build():
    c = canvas.Canvas(str(OUT), pagesize=(W, H))
    c.setTitle("HonestFlow - одностраничное коммерческое предложение")
    c.setAuthor("HonestFlow")

    c.setFillColor(PAPER)
    c.rect(0, 0, W, H, fill=1, stroke=0)
    c.setFillColor(NAVY)
    c.rect(0, H - 34, W, 34, fill=1, stroke=0)
    c.setFillColor(BLUE)
    c.rect(0, 0, 8, H, fill=1, stroke=0)
    c.setFont("S", 9)
    c.setFillColor(WHITE)
    c.drawString(36, H - 22, "HONESTFLOW")
    c.setFillColor(HexColor("#AFC5E8"))
    c.drawRightString(W - 34, H - 22, "ИНЖЕНЕРНЫЙ ИНСТРУМЕНТ ДЛЯ РАБОЧИХ МЕСТ МАРКИРОВКИ")

    # Hero
    c.setFillColor(NAVY)
    c.setFont("B", 23)
    c.drawString(36, H - 73, "Сокращает время восстановления торговой точки")
    para(c, "Типовую программную проблему HonestFlow помогает закрыть на месте. Нестандартную - передаёт инженеру уже с диагностикой и готовым удалённым доступом.", 36, H - 96, W - 72, 10.2, 14, MUTED, "R")

    # Why / why buy
    top = H - 132
    left_w = 300
    card(c, 36, top - 154, left_w, 154)
    pill(c, 50, top - 30, "ЗАЧЕМ HONESTFLOW", BLUE)
    y = top - 55
    points = [
        ("Сбой понятен сразу", "Проверяет ЛМ, ЕСМ, контроллер, ККТ, службы и RuDesktop."),
        ("Типовые действия выполняются на месте", "Запуск служб, установка, инициализация ЛМ, переустановка."),
        ("Инженер начинает не с нуля", "HF собирает логи, статусы, ID RuDesktop и формирует заявку."),
    ]
    for head, body in points:
        c.setFillColor(GREEN)
        c.circle(53, y + 2, 3.2, fill=1, stroke=0)
        c.setFont("S", 8.6)
        c.setFillColor(NAVY)
        c.drawString(64, y, head)
        y = para(c, body, 64, y - 13, left_w - 82, 7.6, 9.5, MUTED, "R") - 7

    mid_x = 350
    mid_w = 214
    card(c, mid_x, top - 154, mid_w, 154, NAVY, NAVY)
    pill(c, mid_x + 14, top - 30, "ПОЧЕМУ СТОИТ КУПИТЬ", CYAN)
    para(c, "«Открываю, когда появляется проблема.»", mid_x + 15, top - 58, mid_w - 30, 13.2, 17, WHITE, "S")
    c.setFont("R", 7.2)
    c.setFillColor(HexColor("#AFC5E8"))
    c.drawString(mid_x + 15, top - 96, "Формулировка пользователя HonestFlow")
    para(c, "Около 90 магазинов уже используют HF. Наиболее востребованы восстановление служб, переустановка компонентов и запрос помощи.", mid_x + 15, top - 116, mid_w - 30, 7.7, 10, WHITE, "R")

    econ_x = 578
    econ_w = W - econ_x - 34
    card(c, econ_x, top - 154, econ_w, 154)
    pill(c, econ_x + 14, top - 30, "ЭКОНОМИКА", AMBER)
    c.setFillColor(NAVY)
    c.setFont("B", 24)
    c.drawString(econ_x + 15, top - 69, "5 400 ₽")
    c.setFillColor(MUTED)
    c.setFont("R", 7.6)
    c.drawString(econ_x + 16, top - 85, "Base за точку в год")
    c.setStrokeColor(LINE)
    c.line(econ_x + 15, top - 99, econ_x + econ_w - 15, top - 99)
    c.setFont("S", 8.5)
    c.setFillColor(AMBER)
    c.drawString(econ_x + 15, top - 119, "600 ₽")
    c.setFillColor(INK)
    c.drawString(econ_x + 62, top - 119, "подключение инженера в Base")
    para(c, "Пользование программой не ограничено. Оплачивается только участие инженера.", econ_x + 15, top - 137, econ_w - 30, 7.3, 9, MUTED, "R")

    # Tariffs
    tariff_y = 54
    tariff_h = 204
    c.setFillColor(NAVY)
    c.setFont("B", 13)
    c.drawString(36, tariff_y + tariff_h + 14, "Сравнение тарифов")
    gap = 14
    tw = (W - 72 - gap) / 2

    card(c, 36, tariff_y, tw, tariff_h, WHITE, BLUE, 12)
    pill(c, 52, tariff_y + tariff_h - 31, "BASE", BLUE)
    c.setFillColor(NAVY)
    c.setFont("B", 20)
    c.drawString(52, tariff_y + tariff_h - 67, "5 400 ₽ / год")
    c.setFillColor(MUTED)
    c.setFont("R", 7.5)
    c.drawString(52, tariff_y + tariff_h - 82, "Полное пользование HonestFlow")
    y = tariff_y + tariff_h - 108
    for t in [
        "Все функции программы без ограничений",
        "Диагностика и исправление типовых проблем",
        "Установка, обновление и обслуживание компонентов",
        "RuDesktop, диагностика и готовая заявка",
        "Подключение инженера - 600 ₽ за каждое подключение",
    ]:
        y = bullet(c, 52, y, t, tw - 32)

    x2 = 36 + tw + gap
    card(c, x2, tariff_y, tw, tariff_h, NAVY, NAVY, 12)
    pill(c, x2 + 16, tariff_y + tariff_h - 31, "EXTENDED", CYAN)
    c.setFillColor(WHITE)
    c.setFont("B", 18)
    c.drawString(x2 + 16, tariff_y + tariff_h - 67, "7 800 ₽ / год")
    c.setFillColor(HexColor("#AFC5E8"))
    c.setFont("R", 7.5)
    c.drawString(x2 + 16, tariff_y + tariff_h - 82, "или 2 500 ₽ / квартал")
    y = tariff_y + tariff_h - 108
    for t in [
        "Все функции HonestFlow без ограничений",
        "Подключения инженера включены в тариф",
        "Удалённая диагностика и устранение проблемы",
        "Повышенный приоритет технической поддержки",
        "Подготовленная заявка и быстрый старт работ",
    ]:
        y = bullet(c, x2 + 16, y, t, tw - 32, CYAN, WHITE)

    c.setStrokeColor(LINE)
    c.line(36, 38, W - 34, 38)
    para(c, "Граница продукта: HF не исправляет физическую неисправность ККТ, отсутствие сети или регистрацию ЕСМ. В этих случаях он локализует проблему и готовит инцидент для инженера.", 36, 28, W - 70, 6.9, 8, MUTED, "R")

    c.showPage()
    c.save()
    print(OUT)


if __name__ == "__main__":
    build()
