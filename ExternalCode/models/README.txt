视觉模型目录

默认文件名：

1. best.pt
   YOLO 检测模型。

2. sam2_b.pt
   SAM 分割模型。

两个模型都存在且 Ultralytics/Torch 可正常导入时，视觉服务会显示处理后的图像。
模型缺失、加载失败或连续处理失败时，服务会自动显示普通 RealSense 实时图像。

如需使用其他位置，可在启动 Python 前设置：

VISION_DETECTION_MODEL=/绝对路径/best.pt
VISION_SAM_MODEL=/绝对路径/sam2_b.pt

