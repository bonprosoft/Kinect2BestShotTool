Imports System.ComponentModel
Imports Microsoft.Kinect
Imports Microsoft.Kinect.Face
Imports System.Globalization


Class MainWindow
    Implements INotifyPropertyChanged

#Region "Properties"
    ' Kinect Readers
    Private kinectSensor As KinectSensor = Nothing
    Private coordinateMapper As CoordinateMapper = Nothing
    Private multiFrameSourceReader As MultiSourceFrameReader = Nothing
    Private faceSource As Face.FaceFrameSource = Nothing
    Private faceReader As Face.FaceFrameReader = Nothing


    ' Bone表示関連
    Private Const HandSize As Double = 30
    Private Const JointThickness As Double = 3
    Private Const ClipBoundsThickness As Double = 10
    Private Const InferredZPositionClamp As Single = 0.1F

    Private ReadOnly handClosedBrush As Brush = New SolidColorBrush(Color.FromArgb(128, 255, 0, 0))
    Private ReadOnly handOpenBrush As Brush = New SolidColorBrush(Color.FromArgb(128, 0, 255, 0))
    Private ReadOnly handLassoBrush As Brush = New SolidColorBrush(Color.FromArgb(128, 0, 0, 255))
    Private ReadOnly trackedJointBrush As Brush = New SolidColorBrush(Color.FromArgb(255, 68, 192, 68))
    Private ReadOnly inferredJointBrush As Brush = Brushes.Yellow
    Private ReadOnly inferredBonePen As New Pen(Brushes.Gray, 1)

    Private drawingGroup As DrawingGroup
    Private bodies As Body() = Nothing
    Private bones As List(Of Tuple(Of JointType, JointType))
    Private displayWidth As Integer
    Private displayHeight As Integer
    Private bodyColors As List(Of Pen)

    ' Body表示関連
    Private Const OpaquePixel As Integer = -1
    Private ReadOnly bytesPerPixel As Integer = Math.Floor((PixelFormats.Bgr32.BitsPerPixel + 7) / 8)

    Private depthFrameData As UShort() = Nothing
    Private colorFrameData As Byte() = Nothing
    Private bodyIndexFrameData As Byte() = Nothing
    Private displayPixels As Byte() = Nothing
    Private colorPoints As ColorSpacePoint() = Nothing

    ' 画面更新用プロパティ
    Private _boneImageSource As DrawingImage
    Private bodyBitmap As WriteableBitmap = Nothing

    Private _statusText As String = Nothing

    Private _isHandValid As Boolean = False
    Private _leftHandFeature As String
    Private _rightHandFeature As String

    Private isLeftHandValid As Boolean = False
    Private isRightHandValid As Boolean = False

    Private _isFaceValid As Boolean = False
    Private _faceFeature As String

    Private isCreatedSnapShot As Boolean = False

    Public ReadOnly Property BoneImageSource() As ImageSource
        Get
            Return Me._boneImageSource
        End Get
    End Property

    Public ReadOnly Property BodyImageSource() As ImageSource
        Get
            Return Me.bodyBitmap
        End Get
    End Property

    Public Property StatusText() As String
        Get
            Return Me._statusText
        End Get

        Set(value As String)
            If Me.StatusText <> value Then
                Me._statusText = value

                RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs("StatusText"))
            End If
        End Set
    End Property

    Public ReadOnly Property IsFaceValid As Boolean
        Get
            Return _isFaceValid
        End Get
    End Property

    Public ReadOnly Property IsHandValid As Boolean
        Get
            Return _isHandValid
        End Get
    End Property

    Public Property FaceFeature As String
        Get
            Return _faceFeature
        End Get
        Set(value As String)
            _faceFeature = value

            Select Case value
                Case DetectionResult.Yes.ToString, DetectionResult.Maybe.ToString()
                    _isFaceValid = True
                Case Else
                    _isFaceValid = False
            End Select

            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs("FaceFeature"))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs("IsFaceValid"))

        End Set
    End Property

    Public Property LeftHandFeature As String
        Get
            Return _leftHandFeature
        End Get
        Set(value As String)
            _leftHandFeature = value

            Select Case value
                Case HandState.Lasso.ToString
                    Me.isLeftHandValid = True
                Case Else
                    Me.isLeftHandValid = False
            End Select

            Me._isHandValid = Me.isLeftHandValid Or Me.isRightHandValid
            ' 更新を通知
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs("LeftHandFeature"))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs("IsHandValid"))

        End Set
    End Property

    Public Property RightHandFeature As String
        Get
            Return _rightHandFeature
        End Get
        Set(value As String)
            _rightHandFeature = value

            Select Case value
                Case HandState.Lasso.ToString
                    Me.isRightHandValid = True
                Case Else
                    Me.isRightHandValid = False
            End Select

            Me._isHandValid = Me.isLeftHandValid Or Me.isRightHandValid
            ' 更新を通知
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs("RightHandFeature"))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs("IsHandValid"))

        End Set
    End Property

#End Region

    Public Sub New()
        ' Kinectセンサーを取得
        Me.kinectSensor = Microsoft.Kinect.KinectSensor.GetDefault()
        ' Coordinate Mapperを取得
        Me.coordinateMapper = Me.kinectSensor.CoordinateMapper

        Dim frameDescription As FrameDescription = Me.kinectSensor.DepthFrameSource.FrameDescription

        Me.displayWidth = frameDescription.Width
        Me.displayHeight = frameDescription.Height

        ' Bone Viewの初期化
        Me.bones = New List(Of Tuple(Of JointType, JointType))()

        ' Torso
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.Head, JointType.Neck))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.Neck, JointType.SpineShoulder))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.SpineShoulder, JointType.SpineMid))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.SpineMid, JointType.SpineBase))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.SpineShoulder, JointType.ShoulderRight))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.SpineShoulder, JointType.ShoulderLeft))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.SpineBase, JointType.HipRight))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.SpineBase, JointType.HipLeft))

        ' Right Arm
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.ShoulderRight, JointType.ElbowRight))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.ElbowRight, JointType.WristRight))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.WristRight, JointType.HandRight))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.HandRight, JointType.HandTipRight))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.WristRight, JointType.ThumbRight))

        ' Left Arm
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.ShoulderLeft, JointType.ElbowLeft))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.ElbowLeft, JointType.WristLeft))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.WristLeft, JointType.HandLeft))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.HandLeft, JointType.HandTipLeft))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.WristLeft, JointType.ThumbLeft))

        ' Right Leg
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.HipRight, JointType.KneeRight))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.KneeRight, JointType.AnkleRight))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.AnkleRight, JointType.FootRight))

        ' Left Leg
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.HipLeft, JointType.KneeLeft))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.KneeLeft, JointType.AnkleLeft))
        Me.bones.Add(New Tuple(Of JointType, JointType)(JointType.AnkleLeft, JointType.FootLeft))

        Me.bodyColors = New List(Of Pen)()

        Me.bodyColors.Add(New Pen(Brushes.Red, 6))
        Me.bodyColors.Add(New Pen(Brushes.Orange, 6))
        Me.bodyColors.Add(New Pen(Brushes.Green, 6))
        Me.bodyColors.Add(New Pen(Brushes.Blue, 6))
        Me.bodyColors.Add(New Pen(Brushes.Indigo, 6))
        Me.bodyColors.Add(New Pen(Brushes.Violet, 6))

        ' MultiFrameSourceReaderを初期化
        Me.multiFrameSourceReader = Me.kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Depth Or FrameSourceTypes.Color Or FrameSourceTypes.BodyIndex Or FrameSourceTypes.Body)
        AddHandler Me.multiFrameSourceReader.MultiSourceFrameArrived, AddressOf Reader_MultiSourceFrameArrived

        ' 受信したデータを格納する用の配列を作成
        Me.depthFrameData = New UShort(Me.displayWidth * Me.displayHeight - 1) {}
        Me.bodyIndexFrameData = New Byte(Me.displayWidth * Me.displayHeight - 1) {}
        Me.displayPixels = New Byte(Me.displayWidth * Me.displayHeight * Me.bytesPerPixel - 1) {}
        Me.colorPoints = New ColorSpacePoint(Me.displayWidth * Me.displayHeight - 1) {}

        ' 表示用のBitmapを初期化
        Me.bodyBitmap = New WriteableBitmap(Me.displayWidth, Me.displayHeight, 96.0, 96.0, PixelFormats.Bgra32, Nothing)

        ' ColorFrameSourceからFrameDescriptionを取得
        Dim colorFrameDescription As FrameDescription = Me.kinectSensor.ColorFrameSource.FrameDescription

        Dim colorWidth As Integer = colorFrameDescription.Width
        Dim colorHeight As Integer = colorFrameDescription.Height

        Me.colorFrameData = New Byte(colorWidth * colorHeight * Me.bytesPerPixel - 1) {}

        ' Kinectセンサーの状態に関するイベントハンドラ
        AddHandler Me.kinectSensor.IsAvailableChanged, AddressOf Me.Sensor_IsAvailableChanged

        ' センサーを開く
        Me.kinectSensor.Open()

        ' センサの現在の状態を取得
        Me.StatusText = If(Me.kinectSensor.IsAvailable, My.Resources.RunningStatusText, My.Resources.NoSensorStatusText)

        ' Boneの際に用いるDrawingグループを作成
        Me.drawingGroup = New DrawingGroup()

        ' ビューに表示するためのDrawingImageを作成
        Me._boneImageSource = New DrawingImage(Me.drawingGroup)

        Me.DataContext = Me

        ' この呼び出しはデザイナーで必要です。
        InitializeComponent()

    End Sub

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Private Sub MainWindow_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        If Me.multiFrameSourceReader IsNot Nothing Then
            Me.multiFrameSourceReader.Dispose()
            Me.multiFrameSourceReader = Nothing
        End If

        If Me.kinectSensor IsNot Nothing Then
            Me.kinectSensor.Close()
            Me.kinectSensor = Nothing
        End If
    End Sub

    ''' <summary>
    ''' センサから取得した、depth/color/body Index Frame DataとBody Frame Dataを処理します
    ''' </summary>
    Private Sub Reader_MultiSourceFrameArrived(sender As Object, e As MultiSourceFrameArrivedEventArgs)
        Dim depthWidth As Integer = 0
        Dim depthHeight As Integer = 0

        Dim colorWidth As Integer = 0
        Dim colorHeight As Integer = 0

        Dim bodyIndexWidth As Integer = 0
        Dim bodyIndexHeight As Integer = 0

        Dim multiSourceFrameProcessed As Boolean = False
        Dim colorFrameProcessed As Boolean = False
        Dim depthFrameProcessed As Boolean = False
        Dim bodyIndexFrameProcessed As Boolean = False
        Dim bodyFrameProcessed As Boolean = False

        Dim multiSourceFrame As MultiSourceFrame = e.FrameReference.AcquireFrame()

        If multiSourceFrame IsNot Nothing Then

            Using depthFrame As DepthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame()
                Using colorFrame As ColorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame()
                    Using bodyIndexFrame As BodyIndexFrame = multiSourceFrame.BodyIndexFrameReference.AcquireFrame()
                        Using BodyFrame As BodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame()

                            If depthFrame IsNot Nothing Then
                                Dim depthFrameDescription As FrameDescription = depthFrame.FrameDescription
                                depthWidth = depthFrameDescription.Width
                                depthHeight = depthFrameDescription.Height

                                If (depthWidth * depthHeight) = Me.depthFrameData.Length Then
                                    depthFrame.CopyFrameDataToArray(Me.depthFrameData)
                                    depthFrameProcessed = True
                                End If
                            End If

                            If colorFrame IsNot Nothing Then
                                Dim colorFrameDescription As FrameDescription = colorFrame.FrameDescription
                                colorWidth = colorFrameDescription.Width
                                colorHeight = colorFrameDescription.Height

                                If (colorWidth * colorHeight * Me.bytesPerPixel) = Me.colorFrameData.Length Then
                                    If colorFrame.RawColorImageFormat = ColorImageFormat.Bgra Then
                                        colorFrame.CopyRawFrameDataToArray(Me.colorFrameData)
                                    Else
                                        colorFrame.CopyConvertedFrameDataToArray(Me.colorFrameData, ColorImageFormat.Bgra)
                                    End If

                                    colorFrameProcessed = True
                                End If
                            End If

                            If bodyIndexFrame IsNot Nothing Then
                                Dim bodyIndexFrameDescription As FrameDescription = bodyIndexFrame.FrameDescription
                                bodyIndexWidth = bodyIndexFrameDescription.Width
                                bodyIndexHeight = bodyIndexFrameDescription.Height

                                If (bodyIndexWidth * bodyIndexHeight) = Me.bodyIndexFrameData.Length Then
                                    bodyIndexFrame.CopyFrameDataToArray(Me.bodyIndexFrameData)

                                    bodyIndexFrameProcessed = True
                                End If
                            End If

                            If BodyFrame IsNot Nothing Then
                                If Me.bodies Is Nothing Then
                                    Me.bodies = New Body(BodyFrame.BodyCount - 1) {}
                                End If

                                BodyFrame.GetAndRefreshBodyData(Me.bodies)
                                bodyFrameProcessed = True
                            End If


                            multiSourceFrameProcessed = True
                        End Using
                    End Using
                End Using
            End Using
        End If

        ' フレームが全て揃った時
        If multiSourceFrameProcessed AndAlso depthFrameProcessed AndAlso colorFrameProcessed AndAlso bodyIndexFrameProcessed Then
            Me.coordinateMapper.MapDepthFrameToColorSpace(Me.depthFrameData, Me.colorPoints)

            Array.Clear(Me.displayPixels, 0, Me.displayPixels.Length)

            ' RowとColumnごとにDepthを見ていく
            For y As Integer = 0 To depthHeight - 1
                For x As Integer = 0 To depthWidth - 1
                    Dim depthIndex As Integer = (y * depthWidth) + x

                    Dim player As Byte = Me.bodyIndexFrameData(depthIndex)

                    If player <> &HFF Then
                        Dim colorPoint As ColorSpacePoint = Me.colorPoints(depthIndex)

                        Dim colorX As Integer = CInt(Math.Floor(colorPoint.X + 0.5))
                        Dim colorY As Integer = CInt(Math.Floor(colorPoint.Y + 0.5))
                        If (colorX >= 0) AndAlso (colorX < colorWidth) AndAlso (colorY >= 0) AndAlso (colorY < colorHeight) Then
                            Dim colorIndex As Integer = ((colorY * colorWidth) + colorX) * Me.bytesPerPixel

                            Dim displayIndex As Integer = depthIndex * Me.bytesPerPixel

                            ' write out blue byte
                            Me.displayPixels(displayIndex) = Me.colorFrameData(colorIndex)
                            displayIndex += 1
                            colorIndex += 1

                            ' write out green byte
                            Me.displayPixels(displayIndex) = Me.colorFrameData(colorIndex)
                            displayIndex += 1
                            colorIndex += 1

                            ' write out red byte
                            Me.displayPixels(displayIndex) = Me.colorFrameData(colorIndex)
                            displayIndex += 1

                            ' write out alpha byte
                            Me.displayPixels(displayIndex) = &HFF
                        End If
                    End If
                Next
            Next

            Me.RenderColorPixels()
        End If

        If bodyFrameProcessed Then
            Using dc As DrawingContext = Me.drawingGroup.Open()
                dc.DrawRectangle(Brushes.Black, Nothing, New Rect(0.0, 0.0, Me.displayWidth, Me.displayHeight))

                Dim penIndex As Integer = 0
                For Each body As Body In Me.bodies
                    Dim drawPen As Pen = Me.bodyColors(penIndex)
                    penIndex += 1

                    If body.IsTracked Then

                        If Me.faceSource Is Nothing Then
                            Me.faceSource = New FaceFrameSource(Me.kinectSensor) With {
                                .FaceFrameFeatures = FaceFrameFeatures.Happy,
                                .TrackingId = body.TrackingId}
                            Me.faceReader = Me.faceSource.OpenReader
                            AddHandler Me.faceReader.FrameArrived, AddressOf Reader_OnFaceFrameArrived
                            AddHandler Me.faceSource.TrackingIdLost, AddressOf Reader_OnTrackingLost
                        End If

                        Me.DrawClippedEdges(body, dc)
                        Dim joints As IReadOnlyDictionary(Of JointType, Joint) = body.Joints

                        Dim jointPoints As New Dictionary(Of JointType, Point)()

                        For Each jointType As JointType In joints.Keys
                            Dim position As CameraSpacePoint = joints(jointType).Position
                            If position.Z < 0 Then
                                position.Z = InferredZPositionClamp
                            End If

                            Dim depthSpacePoint As DepthSpacePoint = Me.coordinateMapper.MapCameraPointToDepthSpace(position)
                            jointPoints(jointType) = New Point(depthSpacePoint.X, depthSpacePoint.Y)
                        Next

                        Me.DrawBody(joints, jointPoints, dc, drawPen)

                        Me.DrawHand(body.HandLeftState, jointPoints(JointType.HandLeft), dc)
                        Me.LeftHandFeature = body.HandLeftState.ToString
                        Me.DrawHand(body.HandRightState, jointPoints(JointType.HandRight), dc)
                        Me.RightHandFeature = body.HandRightState.ToString
                    End If
                Next

                Me.drawingGroup.ClipGeometry = New RectangleGeometry(New Rect(0.0, 0.0, Me.displayWidth, Me.displayHeight))
            End Using
        End If

    End Sub

    ''' <summary>
    ''' FaceTrackingデータを処理します
    ''' </summary>
    ''' <remarks></remarks>
    Private Sub Reader_OnFaceFrameArrived(sender As Object, e As FaceFrameArrivedEventArgs)
        Dim faceFrame = e.FrameReference.AcquireFrame

        If faceFrame IsNot Nothing Then
            Dim result = faceFrame.FaceFrameResult.FaceProperties(FaceProperty.Happy)
            Me.FaceFeature = result.ToString
            If result = DetectionResult.Yes Or result = DetectionResult.Maybe Then
                ' まだSnapShotが作成されていない場合
                If Me.IsHandValid AndAlso Not Me.isCreatedSnapShot Then
                    ' SnapShotを作成 & 現在Trackingしている顔ではこれ以上スナップショットを撮らない
                    Me.isCreatedSnapShot = True
                    CreateSnapShot()
                End If
            End If

        End If

    End Sub

    ''' <summary>
    ''' FaceTrackingにおいて、対象を失ってしまった時の処理を行います
    ''' </summary>
    Private Sub Reader_OnTrackingLost(sender As Object, e As TrackingIdLostEventArgs)
        Me.FaceFeature = "No face tracked"

        Me.faceReader = Nothing
        Me.faceSource = Nothing

        Console.WriteLine(e.TrackingId)

        ' スナップショットの作成状態を初期化
        Me.isCreatedSnapShot = False
    End Sub


    ''' <summary>
    ''' ColorPixelをBitmapに書き込みます
    ''' </summary>
    Private Sub RenderColorPixels()
        Me.bodyBitmap.WritePixels(New Int32Rect(0, 0, Me.bodyBitmap.PixelWidth, Me.bodyBitmap.PixelHeight), Me.displayPixels, Me.bodyBitmap.PixelWidth * Me.bytesPerPixel, 0)
    End Sub


    ''' <summary>
    ''' 体を描画します
    ''' </summary>
    Private Sub DrawBody(joints As IReadOnlyDictionary(Of JointType, Joint), jointPoints As IDictionary(Of JointType, Point), drawingContext As DrawingContext, drawingPen As Pen)
        ' Boneを描画します
        For Each bone In Me.bones
            Me.DrawBone(joints, jointPoints, bone.Item1, bone.Item2, drawingContext, drawingPen)
        Next

        ' 関節を描画します
        For Each jointType As JointType In joints.Keys
            Dim drawBrush As Brush = Nothing

            Dim trackingState As TrackingState = joints(jointType).TrackingState

            If trackingState = trackingState.Tracked Then
                drawBrush = Me.trackedJointBrush
            ElseIf trackingState = trackingState.Inferred Then
                drawBrush = Me.inferredJointBrush
            End If

            If drawBrush IsNot Nothing Then
                drawingContext.DrawEllipse(drawBrush, Nothing, jointPoints(jointType), JointThickness, JointThickness)
            End If
        Next
    End Sub

    ''' <summary>
    ''' 1人のBodyのBoneを描画します
    ''' </summary>
    Private Sub DrawBone(joints As IReadOnlyDictionary(Of JointType, Joint), jointPoints As IDictionary(Of JointType, Point), jointType0 As JointType, jointType1 As JointType, drawingContext As DrawingContext, drawingPen As Pen)
        Dim joint0 As Joint = joints(jointType0)
        Dim joint1 As Joint = joints(jointType1)

        If joint0.TrackingState = TrackingState.NotTracked OrElse joint1.TrackingState = TrackingState.NotTracked Then
            Return
        End If

        Dim drawPen As Pen = Me.inferredBonePen
        If (joint0.TrackingState = TrackingState.Tracked) AndAlso (joint1.TrackingState = TrackingState.Tracked) Then
            drawPen = drawingPen
        End If

        drawingContext.DrawLine(drawPen, jointPoints(jointType0), jointPoints(jointType1))
    End Sub

    ''' <summary>
    ''' もし手がTrackingされていれば描画します red circle = closed, green circle = opened; blue circle = lasso
    ''' </summary>
    Private Sub DrawHand(handState As HandState, handPosition As Point, drawingContext As DrawingContext)
        Select Case handState
            Case handState.Closed
                drawingContext.DrawEllipse(Me.handClosedBrush, Nothing, handPosition, HandSize, HandSize)
                Exit Select

            Case handState.Open
                drawingContext.DrawEllipse(Me.handOpenBrush, Nothing, handPosition, HandSize, HandSize)
                Exit Select

            Case handState.Lasso
                drawingContext.DrawEllipse(Me.handLassoBrush, Nothing, handPosition, HandSize, HandSize)
                Exit Select
        End Select
    End Sub

    ''' <summary>
    ''' どのエッジがクリッピングしたデータであるかを示すためのインジケータを描画します
    ''' </summary>
    Private Sub DrawClippedEdges(body As Body, drawingContext As DrawingContext)
        Dim clippedEdges As FrameEdges = body.ClippedEdges

        If clippedEdges.HasFlag(FrameEdges.Bottom) Then
            drawingContext.DrawRectangle(Brushes.Red, Nothing, New Rect(0, Me.displayHeight - ClipBoundsThickness, Me.displayWidth, ClipBoundsThickness))
        End If

        If clippedEdges.HasFlag(FrameEdges.Top) Then
            drawingContext.DrawRectangle(Brushes.Red, Nothing, New Rect(0, 0, Me.displayWidth, ClipBoundsThickness))
        End If

        If clippedEdges.HasFlag(FrameEdges.Left) Then
            drawingContext.DrawRectangle(Brushes.Red, Nothing, New Rect(0, 0, ClipBoundsThickness, Me.displayHeight))
        End If

        If clippedEdges.HasFlag(FrameEdges.Right) Then
            drawingContext.DrawRectangle(Brushes.Red, Nothing, New Rect(Me.displayWidth - ClipBoundsThickness, 0, ClipBoundsThickness, Me.displayHeight))
        End If
    End Sub

    ''' <summary>
    ''' センサーの状態を更新します
    ''' </summary>
    Private Sub Sensor_IsAvailableChanged(sender As Object, e As IsAvailableChangedEventArgs)
        Me.StatusText = If(Me.kinectSensor.IsAvailable, My.Resources.RunningStatusText, My.Resources.SensorNotAvailableStatusText)
    End Sub

    Private Sub CreateSnapShot()
        ' レンダー先を作成
        Dim renderBitmap As New RenderTargetBitmap(CInt(CompositeImage.ActualWidth), CInt(CompositeImage.ActualHeight), 96.0, 96.0, PixelFormats.Pbgra32)

        Dim dv As New DrawingVisual()
        Using dc As DrawingContext = dv.RenderOpen()
            Dim brush As New VisualBrush(CompositeImage)
            dc.DrawRectangle(brush, Nothing, New Rect(New Point(), New Size(CompositeImage.ActualWidth, CompositeImage.ActualHeight)))
        End Using

        renderBitmap.Render(dv)

        ' Pngエンコーダを初期化
        Dim encoder As BitmapEncoder = New PngBitmapEncoder()
        encoder.Frames.Add(BitmapFrame.Create(renderBitmap))

        Dim time As String = System.DateTime.Now.ToString("hh'-'mm'-'ss", CultureInfo.CurrentUICulture.DateTimeFormat)
        Dim myPhotos As String = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        Dim path As String = IO.Path.Combine(myPhotos, (Convert.ToString("KinectScreenshot-") & time) + ".png")

        Try
            ' Fileに保存
            Using fs As New IO.FileStream(path, IO.FileMode.Create)
                encoder.Save(fs)
            End Using

            Me.StatusText = String.Format(My.Resources.SavedScreenshotStatusTextFormat, path)
        Catch generatedExceptionName As IO.IOException
            Me.StatusText = String.Format(My.Resources.FailedScreenshotStatusTextFormat, path)
        End Try
    End Sub


End Class
