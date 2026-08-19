import pyrealsense2 as rs
import numpy as np
import cv2
import time
import os

from ultralytics import YOLO, SAM



# =====================================================
# YOLO模型
# =====================================================

det_model = YOLO(
    r"F:\YOLO11\ultralytics-main\runs\detect\train-27\weights\best.pt"
)



# =====================================================
# SAM2模型
# =====================================================

sam_model = SAM(
    "sam2_b.pt"
)



# =====================================================
# RealSense初始化
# =====================================================

pipeline = rs.pipeline()

config = rs.config()



config.enable_stream(
    rs.stream.color,
    1280,
    720,
    rs.format.bgr8,
    30
)



config.enable_stream(
    rs.stream.depth,
    1280,
    720,
    rs.format.z16,
    30
)



profile = pipeline.start(
    config
)



# =====================================================
# 深度和彩色对齐
# =====================================================

align = rs.align(
    rs.stream.color
)



# =====================================================
# 深度比例
# =====================================================

depth_sensor = (
    profile
    .get_device()
    .first_depth_sensor()
)


depth_scale = (
    depth_sensor
    .get_depth_scale()
)



print("="*60)

print(
    "Depth Scale:",
    depth_scale
)

print("="*60)




# =====================================================
# 获取相机内参
# =====================================================

color_profile = (
    profile
    .get_stream(
        rs.stream.color
    )
    .as_video_stream_profile()
)



intr = color_profile.get_intrinsics()



fx = intr.fx
fy = intr.fy

cx = intr.ppx
cy = intr.ppy



print("Camera Intrinsics")

print("fx:",fx)

print("fy:",fy)

print("cx:",cx)

print("cy:",cy)



print("="*60)




# =====================================================
# 保存目录
# =====================================================

save_dir = (
    r"F:\YOLO11\ultralytics-main\center_result"
)


os.makedirs(
    save_dir,
    exist_ok=True
)



print(
    "保存目录:",
    save_dir
)



print("="*60)

print("s 保存")

print("q 退出")

print("="*60)




# =====================================================
# 深度获取函数
# 11×11邻域中值
# =====================================================

def get_depth(
    depth_frame,
    u,
    v
):

    values=[]


    for dy in range(-5,6):

        for dx in range(-5,6):


            uu = u + dx

            vv = v + dy



            if uu < 0 or vv < 0:

                continue



            d = depth_frame.get_distance(
                uu,
                vv
            )



            if d > 0:

                values.append(d)



    if len(values)==0:

        return 0



    return float(
        np.median(values)
    )
# =====================================================
# 主循环
# =====================================================

try:

    while True:


        # =====================================================
        # 获取RGB + Depth
        # =====================================================

        frames = pipeline.wait_for_frames()


        aligned_frames = align.process(
            frames
        )


        color_frame = (
            aligned_frames
            .get_color_frame()
        )


        depth_frame = (
            aligned_frames
            .get_depth_frame()
        )


        if not color_frame or not depth_frame:

            continue



        # =====================================================
        # 转numpy
        # =====================================================

        color_img = np.asanyarray(
            color_frame.get_data()
        )


        depth_img = np.asanyarray(
            depth_frame.get_data()
        )


        result_img = color_img.copy()



        # =====================================================
        # YOLO检测
        # =====================================================

        det_results = det_model.predict(
            source=color_img,
            conf=0.2,
            verbose=False
        )



        if len(det_results[0].boxes)==0:


            cv2.imshow(
                "Center Position",
                result_img
            )


            key=cv2.waitKey(1)&0xff


            if key==ord('q'):

                break


            continue




        # =====================================================
        # 遍历检测目标
        # =====================================================

        for box in (
            det_results[0]
            .boxes
            .xyxy
            .cpu()
            .numpy()
        ):


            x1,y1,x2,y2 = map(
                int,
                box
            )



            # =====================================================
            # SAM2分割
            # =====================================================

            sam_results = sam_model(
                color_img,
                bboxes=[
                    [
                        x1,
                        y1,
                        x2,
                        y2
                    ]
                ]
            )



            if sam_results[0].masks is None:

                continue




            # =====================================================
            # 获取Mask
            # =====================================================

            mask = (
                sam_results[0]
                .masks
                .data[0]
                .cpu()
                .numpy()
            )


            mask = (
                mask*255
            ).astype(
                np.uint8
            )



            # =====================================================
            # 提取最大轮廓
            # =====================================================

            contours,_ = cv2.findContours(
                mask,
                cv2.RETR_EXTERNAL,
                cv2.CHAIN_APPROX_SIMPLE
            )



            if len(contours)==0:

                continue



            cnt=max(
                contours,
                key=cv2.contourArea
            )



            # =====================================================
            # 绘制Mask轮廓
            # =====================================================

            cv2.drawContours(
                result_img,
                [cnt],
                -1,
                (0,255,255),
                2
            )




            # =====================================================
            # 最小外接旋转矩形
            # =====================================================

            rect=cv2.minAreaRect(
                cnt
            )


            # rect:
            #
            # (
            #   (center_x,center_y),
            #   (width,height),
            #   angle
            # )


            (center_x,center_y), (w,h), angle = rect



            center_x=int(
                center_x
            )


            center_y=int(
                center_y
            )



            print("="*60)

            print(
                "旋转矩形中心像素:",
                center_x,
                center_y
            )




            # =====================================================
            # 绘制旋转矩形
            # =====================================================

            box_points=cv2.boxPoints(
                rect
            )


            box_points=np.int32(
                box_points
            )


            cv2.drawContours(
                result_img,
                [
                    box_points
                ],
                0,
                (0,255,0),
                2
            )



            # =====================================================
            # 获取中心点深度
            # =====================================================

            center_depth=get_depth(
                depth_frame,
                center_x,
                center_y
            )



            if center_depth<=0:


                print(
                    "中心点深度无效"
                )


                continue




            print(
                "中心点深度:",
                center_depth,
                "m"
            )




            # =====================================================
            # 像素坐标 -> 相机坐标(mm)
            # =====================================================

            Z=center_depth



            X=(
                (center_x-cx)
                *
                Z
                /
                fx
            )



            Y=(
                (center_y-cy)
                *
                Z
                /
                fy
            )



            center_camera=np.array(
                [
                    X*1000,
                    Y*1000,
                    Z*1000
                ]
            )



            print(
                "Center Camera(mm)"
            )


            print(
                "X = %.2f mm"
                %
                center_camera[0]
            )


            print(
                "Y = %.2f mm"
                %
                center_camera[1]
            )


            print(
                "Z = %.2f mm"
                %
                center_camera[2]
            )


            print("="*60)




            # =====================================================
            # 绘制中心点
            # =====================================================

            cv2.circle(
                result_img,
                (
                    center_x,
                    center_y
                ),
                6,
                (0,0,255),
                -1
            )



            cv2.putText(
                result_img,
                "Center",
                (
                    center_x+10,
                    center_y-20
                ),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.6,
                (0,0,255),
                2
            )



            cv2.putText(
                result_img,
                f"X:{center_camera[0]:.1f}mm",
                (
                    center_x+10,
                    center_y
                ),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.5,
                (0,255,0),
                2
            )


            cv2.putText(
                result_img,
                f"Y:{center_camera[1]:.1f}mm",
                (
                    center_x+10,
                    center_y+20
                ),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.5,
                (0,255,0),
                2
            )


            cv2.putText(
                result_img,
                f"Z:{center_camera[2]:.1f}mm",
                (
                    center_x+10,
                    center_y+40
                ),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.5,
                (0,255,0),
                2
            )
        # =====================================================
        # 深度伪彩色显示
        # =====================================================

        depth_show = cv2.applyColorMap(
            cv2.convertScaleAbs(
                depth_img,
                alpha=0.03
            ),
            cv2.COLORMAP_JET
        )



        # =====================================================
        # 显示窗口
        # =====================================================

        cv2.imshow(
            "Center Position",
            result_img
        )


        cv2.imshow(
            "Depth",
            depth_show
        )



        # =====================================================
        # 键盘监听
        # =====================================================

        key = cv2.waitKey(1) & 0xff




        # =====================================================
        # 保存当前结果
        # =====================================================

        if key == ord('s'):



            timestamp = int(
                time.time()
            )



            color_name=os.path.join(
                save_dir,
                f"{timestamp}_color.png"
            )


            depth_name=os.path.join(
                save_dir,
                f"{timestamp}_depth.png"
            )


            result_name=os.path.join(
                save_dir,
                f"{timestamp}_result.png"
            )


            txt_name=os.path.join(
                save_dir,
                f"{timestamp}_center.txt"
            )



            # 保存RGB

            cv2.imwrite(
                color_name,
                color_img
            )



            # 保存16位深度

            cv2.imwrite(
                depth_name,
                depth_img
            )



            # 保存结果图

            cv2.imwrite(
                result_name,
                result_img
            )




            # =====================================================
            # 保存中心点坐标
            # =====================================================

            with open(
                txt_name,
                "w",
                encoding="utf-8"
            ) as f:



                f.write(
                    "========= Center Camera Coordinate(mm) =========\n"
                )


                if 'center_camera' in locals():


                    f.write(
                        f"X = {center_camera[0]:.3f} mm\n"
                    )


                    f.write(
                        f"Y = {center_camera[1]:.3f} mm\n"
                    )


                    f.write(
                        f"Z = {center_camera[2]:.3f} mm\n"
                    )


                else:


                    f.write(
                        "No valid center point\n"
                    )



            print("="*60)

            print(
                "中心点数据保存完成"
            )

            print(
                txt_name
            )

            print("="*60)




        # =====================================================
        # 退出
        # =====================================================

        elif key == ord('q') or key==27:

            break



# =====================================================
# 程序结束
# =====================================================

finally:


    pipeline.stop()


    cv2.destroyAllWindows()



    print("="*60)

    print(
        "程序结束"
    )

    print("="*60)