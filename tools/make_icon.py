# -*- coding: utf-8 -*-
"""
ScreenshotCollector exe 图标生成脚本（工具脚本，与主程序无关）
生成两个方案的图标：
  方案1 app_icon_pic.ico   原图圆形裁剪 + 白色圆环
  方案2 app_icon_text.ico  纯色圆形(#44546A) + 「王四季」白字（极简）
每个方案同时输出 1024 PNG 预览与多尺寸 ICO（256/128/64/48/32/16）。
"""
import os
from PIL import Image, ImageDraw, ImageFont

BASE = r"C:\Users\NAN SHENG CSUR\source\repos\ScreenshotCollector"
SRC = os.path.join(BASE, r"src\ScreenshotCollector\Assets\Brand\bg.jpg")
OUT = os.path.join(BASE, r"src\ScreenshotCollector\Assets\Brand")

ICON_SIZES = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
SIZE = 1024


def save_ico(img, name):
    path = os.path.join(OUT, name)
    img.save(path, format="ICO", sizes=ICON_SIZES)
    return path


# ---------- 方案1：原图圆形裁剪 + 白色圆环 ----------
img = Image.open(SRC).convert("RGBA").resize((SIZE, SIZE), Image.LANCZOS)
mask = Image.new("L", (SIZE, SIZE), 0)
ImageDraw.Draw(mask).ellipse((0, 0, SIZE, SIZE), fill=255)
circle = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
circle.paste(img, (0, 0), mask)
ring = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
ImageDraw.Draw(ring).ellipse((0, 0, SIZE, SIZE), outline=(255, 255, 255, 255), width=56)
circle = Image.alpha_composite(circle, ring)
circle.save(os.path.join(OUT, "icon_pic.png"))
print("方案1 PNG 已生成")
print("方案1 ICO:", save_ico(circle, "app_icon_pic.ico"))

# ---------- 方案2：纯色圆形 + 王四季（极简） ----------
font = ImageFont.truetype(r"C:\Windows\Fonts\msyhbd.ttc", 280)
canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
d = ImageDraw.Draw(canvas)
d.ellipse((0, 0, SIZE, SIZE), fill=(68, 84, 106, 255))  # #44546A 与程序主色一致
d.text((SIZE / 2, SIZE / 2), "王四季", font=font, fill=(255, 255, 255, 255), anchor="mm")
d.ellipse((30, 30, SIZE - 30, SIZE - 30), outline=(255, 255, 255, 64), width=6)
canvas.save(os.path.join(OUT, "icon_text.png"))
print("方案2 PNG 已生成")
print("方案2 ICO:", save_ico(canvas, "app_icon_text.ico"))
print("完成")
