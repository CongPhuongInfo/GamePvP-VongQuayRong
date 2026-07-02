Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.IO

''' <summary>
''' Game "Vong Quay Rong": toi da 4 nguoi choi (1 Host + 3 Client) cung dat cuoc tren
''' 1 vong quay chung. Moi van: chon 1 trong 12 con vat + so diem cuoc, Host quay ngau
''' nhien (co trong so), ai chon trung con vat thi duoc CUOC x HeSoNhan diem, sai thi mat
''' CUOC diem. Kien truc mang tai su dung nguyen NetworkHub.vb (Host) / NetworkPeer.vb (Client).
''' </summary>
Public Class Form1
    Inherits Form

    Private Const DEFAULT_PORT As Integer = 9050
    Private Const BOARD_W As Integer = 560
    Private Const BOARD_H As Integer = 340
    Private Const CELL_RADIUS As Integer = 34
    Private Const BETTING_SECONDS As Integer = 15

    Private Enum RoundState
        Idle
        Betting
        Spinning
        ShowingResult
    End Enum

    ' ------------------- Mang -------------------
    Private hub As NetworkHub          ' dung khi la Host
    Private peer As NetworkPeer        ' dung khi la Client
    Private isHost As Boolean = False
    Private localSeat As Integer = -1  ' 0 = Host, 1..3 = Client
    Private playerNames(3) As String
    Private playerConnected(3) As Boolean

    ' ------------------- Game -------------------
    Private game As New VongQuayRongGame()
    Private scoresBySeat As New Dictionary(Of Integer, Long)
    Private state As RoundState = RoundState.Idle
    Private secondsLeft As Integer = 0
    Private countdownTimer As Timer
    Private hintTimer As Timer

    Private selectedAnimalIndex As Integer = -1     ' con vat dang chon (chua khoa)
    Private hasLockedThisRound As Boolean = False
    Private lockedAnimalBySeat(3) As Integer        ' -1 = chua khoa
    Private lockedAmountBySeat(3) As Long
    Private lastResultIndex As Integer = -1
    ' cache ket qua van gan nhat da broadcast (dung de dong bo cho nguoi vao phong giua van)
    Private lastBroadcastResultEntries As String = Nothing

    ' cho hoi hop: ket qua nhan duoc tu mang se bi "giu lai", chi cong bo sau khi
    ' hieu ung quay tren MAY NAY chay xong (khong lo diem/thang-thua som).
    Private spinAnimInProgress As Boolean = False
    Private pendingResultIndex As Integer = -1
    Private pendingResultEntries As String = Nothing

    ' hieu ung quay
    Private spinSequence As New List(Of Integer)
    Private spinDelays As New List(Of Integer)
    Private spinPos As Integer = 0
    Private spinTimer As Timer
    Private highlightIndex As Integer = -1
    Private animalCenters(VongQuayRongGame.ANIMAL_COUNT - 1) As PointF
    Private boardRect As Rectangle

    ' ------------------- Anh sprite (Assets\*.png), co the null neu thieu file -------------------
    Private animalImages(VongQuayRongGame.ANIMAL_COUNT - 1) As Image
    Private dragonImage As Image

    ' ------------------- UI: Connect panel -------------------
    Private pnlConnect As Panel
    Private txtName As TextBox
    Private txtIP As TextBox
    Private txtPort As TextBox
    Private btnHost As Button
    Private btnJoin As Button
    Private lblConnectStatus As Label

    ' ------------------- UI: Game panel -------------------
    Private pnlGame As Panel
    Private boardPanel As Panel
    Private lblRoundInfo As Label
    Private lblCountdown As Label
    Private nudBet As NumericUpDown
    Private btnLockBet As Button
    Private btnHostAction As Button
    Private pnlPlayers(3) As Panel
    Private lblCardStatus(3) As Label
    Private lblCardStats(3) As Label
    Private lblCardResult(3) As Label
    Private lastRoundHasResult(3) As Boolean
    Private lastRoundWonBySeat(3) As Boolean
    Private lastRoundPayoutBySeat(3) As Long

    ' ------------------- UI: Chat panel -------------------
    Private pnlChat As Panel
    Private lstChat As ListBox
    Private txtChatInput As TextBox
    Private btnSend As Button

    Public Sub New()
        Me.Text = "Vong Quay Rong"
        Me.ClientSize = New Size(940, 700)
        Me.StartPosition = FormStartPosition.CenterScreen
        Dim i As Integer
        For i = 0 To 3
            playerNames(i) = "Nguoi choi " & (i + 1).ToString()
            playerConnected(i) = False
            scoresBySeat(i) = VongQuayRongGame.STARTING_SCORE
            lockedAnimalBySeat(i) = -1
        Next i
        BuildConnectPanel()
        LoadSprites()
    End Sub

    ''' <summary>Nap anh tu thu muc "Assets" (dat canh file .exe). Neu thieu file hoac loi doc anh,
    ''' con vat/dau rong tuong ung se tu dong ve lai bang hinh tron mau (fallback), khong bi crash.</summary>
    Private Sub LoadSprites()
        Dim assetsDir As String = Path.Combine(Application.StartupPath, "Assets")
        Dim i As Integer
        For i = 0 To VongQuayRongGame.ANIMAL_COUNT - 1
            Dim fileName As String = VongQuayRongGame.Animals(i).ImageFile
            animalImages(i) = Nothing
            If fileName IsNot Nothing AndAlso fileName <> "" Then
                Dim fullPath As String = Path.Combine(assetsDir, fileName)
                Try
                    If File.Exists(fullPath) Then
                        animalImages(i) = Image.FromFile(fullPath)
                    End If
                Catch
                    animalImages(i) = Nothing ' anh loi/hong -> dung fallback ve tay
                End Try
            End If
        Next i

        Try
            Dim dragonPath As String = Path.Combine(assetsDir, "Rong.png")
            If File.Exists(dragonPath) Then
                dragonImage = Image.FromFile(dragonPath)
            End If
        Catch
            dragonImage = Nothing
        End Try
    End Sub

    ' ============================================================
    '  MAU / TEN THEO SEAT
    ' ============================================================
    Private Function PlayerColor(seat As Integer) As Color
        Select Case seat
            Case 0 : Return Color.FromArgb(200, 40, 40)
            Case 1 : Return Color.FromArgb(30, 110, 200)
            Case 2 : Return Color.FromArgb(30, 150, 70)
            Case Else : Return Color.FromArgb(160, 90, 190)
        End Select
    End Function

    ' ============================================================
    '  CONNECT PANEL (chon Host hoac Join)
    ' ============================================================
    Private Sub BuildConnectPanel()
        pnlConnect = New Panel()
        pnlConnect.Dock = DockStyle.Fill
        pnlConnect.BackColor = Color.FromArgb(245, 245, 240)

        Dim lblTitle As New Label()
        lblTitle.Text = "VONG QUAY RONG"
        lblTitle.Font = New Font("Segoe UI", 18.0!, FontStyle.Bold)
        lblTitle.AutoSize = True
        lblTitle.Location = New Point(40, 30)
        pnlConnect.Controls.Add(lblTitle)

        Dim lblName As New Label() : lblName.Text = "Ten cua ban:" : lblName.AutoSize = True
        lblName.Location = New Point(40, 100)
        pnlConnect.Controls.Add(lblName)
        txtName = New TextBox() : txtName.Location = New Point(40, 122) : txtName.Size = New Size(220, 24)
        txtName.Text = "Nguoi choi"
        pnlConnect.Controls.Add(txtName)

        Dim lblPort As New Label() : lblPort.Text = "Cong (Port):" : lblPort.AutoSize = True
        lblPort.Location = New Point(40, 160)
        pnlConnect.Controls.Add(lblPort)
        txtPort = New TextBox() : txtPort.Location = New Point(40, 182) : txtPort.Size = New Size(220, 24)
        txtPort.Text = DEFAULT_PORT.ToString()
        pnlConnect.Controls.Add(txtPort)

        btnHost = New Button() : btnHost.Text = "Tao phong (Host)"
        btnHost.Location = New Point(40, 220) : btnHost.Size = New Size(220, 34)
        AddHandler btnHost.Click, AddressOf BtnHost_Click
        pnlConnect.Controls.Add(btnHost)

        Dim lblIP As New Label() : lblIP.Text = "IP cua Host:" : lblIP.AutoSize = True
        lblIP.Location = New Point(40, 280)
        pnlConnect.Controls.Add(lblIP)
        txtIP = New TextBox() : txtIP.Location = New Point(40, 302) : txtIP.Size = New Size(220, 24)
        txtIP.Text = "127.0.0.1"
        pnlConnect.Controls.Add(txtIP)

        btnJoin = New Button() : btnJoin.Text = "Vao phong (Join)"
        btnJoin.Location = New Point(40, 336) : btnJoin.Size = New Size(220, 34)
        AddHandler btnJoin.Click, AddressOf BtnJoin_Click
        pnlConnect.Controls.Add(btnJoin)

        lblConnectStatus = New Label()
        lblConnectStatus.Location = New Point(40, 390) : lblConnectStatus.Size = New Size(400, 60)
        lblConnectStatus.ForeColor = Color.DimGray
        pnlConnect.Controls.Add(lblConnectStatus)

        Me.Controls.Add(pnlConnect)
    End Sub

    Private Sub BtnHost_Click(sender As Object, e As EventArgs)
        Dim port As Integer
        If Not Integer.TryParse(txtPort.Text.Trim(), port) Then
            MessageBox.Show("Port khong hop le.") : Return
        End If
        isHost = True
        localSeat = 0
        playerNames(0) = SafeName(txtName.Text)
        playerConnected(0) = True

        hub = New NetworkHub(Me)
        AddHandler hub.ClientConnected, AddressOf Hub_ClientConnected
        AddHandler hub.ClientDisconnected, AddressOf Hub_ClientDisconnected
        AddHandler hub.LineReceivedFromClient, AddressOf Hub_LineReceived
        hub.StartListening(port)

        lblConnectStatus.Text = "Dang cho nguoi choi ket noi tren cong " & port.ToString() & " ..."
        ShowGamePanel()
    End Sub

    Private Sub BtnJoin_Click(sender As Object, e As EventArgs)
        Dim port As Integer
        If Not Integer.TryParse(txtPort.Text.Trim(), port) Then
            MessageBox.Show("Port khong hop le.") : Return
        End If
        isHost = False
        playerNames(0) = SafeName(txtName.Text) ' se duoc ghi de dung seat sau khi WELCOME

        peer = New NetworkPeer(Me)
        AddHandler peer.Connected, AddressOf Peer_Connected
        AddHandler peer.Disconnected, AddressOf Peer_Disconnected
        AddHandler peer.LineReceived, AddressOf Peer_LineReceived
        peer.ConnectToHost(txtIP.Text.Trim(), port)

        lblConnectStatus.Text = "Dang ket noi den " & txtIP.Text.Trim() & ":" & port.ToString() & " ..."
    End Sub

    Private Function SafeName(raw As String) As String
        Dim s As String = raw.Trim()
        If s = "" Then Return "Nguoi choi"
        If s.Length > 16 Then s = s.Substring(0, 16)
        Return s
    End Function

    ' ============================================================
    '  SU KIEN MANG - PHIA CLIENT (NetworkPeer)
    ' ============================================================
    Private Sub Peer_Connected()
        peer.SendLine("VQR_HELLO:" & playerNames(0))
    End Sub

    Private Sub Peer_Disconnected()
        AppendChat("[He thong] Mat ket noi toi Host.")
    End Sub

    Private Sub Peer_LineReceived(line As String)
        HandleProtocolLine(line, -1)
    End Sub

    ' ============================================================
    '  SU KIEN MANG - PHIA HOST (NetworkHub)
    ' ============================================================
    Private Sub Hub_ClientConnected(seatIndex As Integer)
        playerConnected(seatIndex) = True
        hub.SendToClient(seatIndex, "VQR_WELCOME:" & seatIndex.ToString())
        BroadcastNames()
        BroadcastScores()
        SyncStateToLateJoiner(seatIndex)
        RefreshPlayerCards()
        AppendChat("[He thong] Player " & (seatIndex + 1).ToString() & " da vao phong.")
    End Sub

    ''' <summary>Neu co nguoi vao phong khi van dau da bat dau roi (Betting/Spinning/ShowingResult),
    ''' gui rieng cho seat do du du lieu de man hinh khong bi ket o "Cho Host bat dau van moi".
    ''' Voi Spinning/ShowingResult, nguoi vao sau se thay ngay ket qua (bo qua hieu ung quay da troi qua),
    ''' vi ho khong co mat luc quay nen khong can hoi hop.</summary>
    Private Sub SyncStateToLateJoiner(seatIndex As Integer)
        If state = RoundState.Idle Then Return

        hub.SendToClient(seatIndex, "VQR_ROUND:" & game.CurrentRoundNo.ToString() & "|" & Math.Max(0, secondsLeft).ToString())

        Dim s As Integer
        For s = 0 To 3
            If lockedAnimalBySeat(s) >= 0 Then
                hub.SendToClient(seatIndex, "VQR_LOCK:" & s.ToString() & "|" & lockedAnimalBySeat(s).ToString() & "|" & lockedAmountBySeat(s).ToString())
            End If
        Next s

        If (state = RoundState.Spinning OrElse state = RoundState.ShowingResult) AndAlso lastBroadcastResultEntries IsNot Nothing Then
            hub.SendToClient(seatIndex, "VQR_RESULT:" & lastResultIndex.ToString() & ":" & lastBroadcastResultEntries)
        End If
    End Sub

    Private Sub Hub_ClientDisconnected(seatIndex As Integer)
        playerConnected(seatIndex) = False
        playerNames(seatIndex) = "Nguoi choi " & (seatIndex + 1).ToString()
        BroadcastNames()
        RefreshPlayerCards()
        AppendChat("[He thong] Player " & (seatIndex + 1).ToString() & " da roi phong.")
    End Sub

    Private Sub Hub_LineReceived(seatIndex As Integer, line As String)
        HandleProtocolLine(line, seatIndex)
    End Sub

    ' ============================================================
    '  XU LY GIAO THUC CHUNG
    '  fromSeat = -1 khi day la Client dang nhan tin tu Host (khong can biet seat nguoi gui)
    '  fromSeat = 0..3 khi day la Host dang nhan tin tu 1 Client cu the (seat do)
    ' ============================================================
    Private Sub HandleProtocolLine(line As String, fromSeat As Integer)
        If line Is Nothing OrElse line = "" Then Return
        Dim idx As Integer = line.IndexOf(":"c)
        Dim msgType As String = If(idx >= 0, line.Substring(0, idx), line)
        Dim payload As String = If(idx >= 0, line.Substring(idx + 1), "")

        Select Case msgType
            Case "CHAT"
                Dim p2 As Integer = payload.IndexOf(":"c)
                If p2 >= 0 Then
                    AppendChat(payload.Substring(0, p2) & ": " & payload.Substring(p2 + 1))
                End If
                If isHost Then hub.BroadcastExcept("CHAT:" & payload, fromSeat)

            Case "VQR_WELCOME"
                localSeat = Integer.Parse(payload, CultureInfo.InvariantCulture)
                ShowGamePanel()
                lblConnectStatus.Text = "Da vao phong, ban la Player " & (localSeat + 1).ToString()

            Case "VQR_HELLO"
                If fromSeat >= 0 Then
                    playerNames(fromSeat) = SafeName(payload)
                    BroadcastNames()
                    RefreshPlayerCards()
                End If

            Case "VQR_NAMES"
                Dim parts As String() = payload.Split("|"c)
                Dim i As Integer
                For i = 0 To Math.Min(3, parts.Length - 1)
                    If parts(i) <> "" Then playerNames(i) = parts(i)
                Next i
                RefreshPlayerCards()

            Case "VQR_SCORES"
                Dim sp As String() = payload.Split("|"c)
                Dim i2 As Integer
                For i2 = 0 To Math.Min(3, sp.Length - 1)
                    Dim v As Long
                    If Long.TryParse(sp(i2), NumberStyles.Integer, CultureInfo.InvariantCulture, v) Then
                        scoresBySeat(i2) = v
                    End If
                Next i2
                RefreshPlayerCards()

            Case "VQR_ROUND"
                Dim rp As String() = payload.Split("|"c)
                Dim roundNo As Integer = Integer.Parse(rp(0), CultureInfo.InvariantCulture)
                Dim secs As Integer = Integer.Parse(rp(1), CultureInfo.InvariantCulture)
                BeginBettingLocal(roundNo, secs)

            Case "VQR_LOCK"
                Dim lp As String() = payload.Split("|"c)
                Dim seat As Integer = Integer.Parse(lp(0), CultureInfo.InvariantCulture)
                Dim animal As Integer = Integer.Parse(lp(1), CultureInfo.InvariantCulture)
                Dim amount As Long = Long.Parse(lp(2), CultureInfo.InvariantCulture)
                lockedAnimalBySeat(seat) = animal
                lockedAmountBySeat(seat) = amount
                RefreshPlayerCards()

            Case "VQR_BET"
                If fromSeat >= 0 AndAlso isHost Then
                    Dim bp As String() = payload.Split("|"c)
                    Dim animal As Integer = Integer.Parse(bp(0), CultureInfo.InvariantCulture)
                    Dim amount As Long = Long.Parse(bp(1), CultureInfo.InvariantCulture)
                    ProcessBetFromSeat(fromSeat, animal, amount)
                End If

            Case "VQR_BET_NACK"
                UnlockLocalBetUI()

            Case "VQR_SPIN"
                Dim resultIndex As Integer = Integer.Parse(payload, CultureInfo.InvariantCulture)
                StartSpinAnimation(resultIndex)

            Case "VQR_RESULT"
                Dim colonIdx As Integer = payload.IndexOf(":"c)
                Dim resIndex As Integer = Integer.Parse(payload.Substring(0, colonIdx), CultureInfo.InvariantCulture)
                Dim entriesRaw As String = payload.Substring(colonIdx + 1)
                QueueOrApplyResult(resIndex, entriesRaw)
        End Select
    End Sub

    ' ============================================================
    '  GUI DU LIEU (Host broadcast trang thai dung cho ca Host lan Client)
    ' ============================================================
    Private Sub BroadcastNames()
        If Not isHost Then Return
        Dim s As String = playerNames(0) & "|" & playerNames(1) & "|" & playerNames(2) & "|" & playerNames(3)
        hub.Broadcast("VQR_NAMES:" & s)
    End Sub

    Private Sub BroadcastScores()
        If Not isHost Then Return
        Dim s As String = scoresBySeat(0).ToString() & "|" & scoresBySeat(1).ToString() & "|" &
                           scoresBySeat(2).ToString() & "|" & scoresBySeat(3).ToString()
        hub.Broadcast("VQR_SCORES:" & s)
    End Sub

    ' ============================================================
    '  VONG DAT CUOC (Host dieu khien state machine)
    ' ============================================================
    Private Sub BtnHostAction_Click(sender As Object, e As EventArgs)
        If Not isHost Then Return
        Select Case state
            Case RoundState.Idle, RoundState.ShowingResult
                game.StartNewRound()
                lastBroadcastResultEntries = Nothing
                Dim i As Integer
                For i = 0 To 3
                    lockedAnimalBySeat(i) = -1
                Next i
                hub.Broadcast("VQR_ROUND:" & game.CurrentRoundNo.ToString() & "|" & BETTING_SECONDS.ToString())
                BeginBettingLocal(game.CurrentRoundNo, BETTING_SECONDS)

            Case RoundState.Betting
                DoSpinNow()

            Case RoundState.Spinning
                ' dang quay, khong lam gi

        End Select
    End Sub

    Private Sub BeginBettingLocal(roundNo As Integer, secs As Integer)
        state = RoundState.Betting
        boardPanel.Cursor = Cursors.Hand
        selectedAnimalIndex = -1
        hasLockedThisRound = False
        Dim i As Integer
        For i = 0 To 3
            lockedAnimalBySeat(i) = -1
            lastRoundHasResult(i) = False
            If pnlPlayers(i) IsNot Nothing Then pnlPlayers(i).BackColor = Color.White
            If lblCardResult(i) IsNot Nothing Then lblCardResult(i).Text = ""
        Next i
        secondsLeft = secs
        lblRoundInfo.Text = "Van " & roundNo.ToString() & " - hay chon 1 con vat va khoa cuoc!"
        lblCountdown.Text = "Con: " & secondsLeft.ToString() & "s"
        btnLockBet.Enabled = True
        nudBet.Enabled = True
        If isHost Then
            btnHostAction.Text = "Quay ngay"
            btnHostAction.Enabled = True
            If countdownTimer Is Nothing Then
                countdownTimer = New Timer()
                countdownTimer.Interval = 1000
                AddHandler countdownTimer.Tick, AddressOf CountdownTimer_Tick
            End If
            countdownTimer.Start()
        Else
            btnHostAction.Visible = False
        End If
        boardPanel.Invalidate()
        RefreshPlayerCards()
    End Sub

    Private Sub CountdownTimer_Tick(sender As Object, e As EventArgs)
        secondsLeft -= 1
        lblCountdown.Text = "Con: " & Math.Max(0, secondsLeft).ToString() & "s"
        If secondsLeft <= 0 Then
            countdownTimer.Stop()
            DoSpinNow()
        End If
    End Sub

    ''' <summary>Chi Host goi: chot van dat cuoc, quay va broadcast ket qua.</summary>
    Private Sub DoSpinNow()
        If Not isHost Then Return
        If countdownTimer IsNot Nothing Then countdownTimer.Stop()
        state = RoundState.Spinning
        boardPanel.Cursor = Cursors.Default
        btnHostAction.Enabled = False
        btnLockBet.Enabled = False

        Dim resultIndex As Integer = game.SpinResult()
        lastResultIndex = resultIndex
        hub.Broadcast("VQR_SPIN:" & resultIndex.ToString())
        StartSpinAnimation(resultIndex) ' Host tu quay hinh anh cua minh luon

        Dim outcomes As List(Of VongQuayRongGame.RoundOutcome) = game.ComputePayouts(resultIndex, scoresBySeat)
        Dim sb As New StringBuilder()
        sb.Append(resultIndex.ToString()).Append(":")
        Dim first As Boolean = True
        For Each o As VongQuayRongGame.RoundOutcome In outcomes
            If Not first Then sb.Append(";")
            first = False
            sb.Append(o.Seat.ToString()).Append(",").Append(o.AnimalIndex.ToString()).Append(",")
            sb.Append(o.Amount.ToString()).Append(",").Append(If(o.Won, "1", "0")).Append(",")
            sb.Append(o.Payout.ToString()).Append(",").Append(o.NewScore.ToString())
        Next o
        hub.Broadcast("VQR_RESULT:" & sb.ToString())

        ' Host cung tu "nhan" ket qua cua chinh minh giong nhu Client, de bi giu lai
        ' cho toi khi vong quay tren man hinh Host dung han (xem QueueOrApplyResult).
        Dim entriesForHost As String = sb.ToString().Substring(sb.ToString().IndexOf(":"c) + 1)
        lastBroadcastResultEntries = entriesForHost
        QueueOrApplyResult(resultIndex, entriesForHost)
    End Sub

    ''' <summary>Host xu ly 1 cuoc gui len tu Client (hoac tu chinh Host qua LockBet local).
    ''' Neu cuoc khong hop le (het gio, da cuoc roi, du lieu sai), bao NACK ve dung seat do
    ''' de UI cua ho duoc mo khoa lai thay vi ket cung.</summary>
    Private Sub ProcessBetFromSeat(seat As Integer, animalIndex As Integer, amount As Long)
        If state <> RoundState.Betting Then
            RejectBet(seat)
            Return
        End If
        If game.HasBet(seat) Then
            RejectBet(seat)
            Return
        End If
        Dim currentScore As Long = 0L
        If scoresBySeat.ContainsKey(seat) Then currentScore = scoresBySeat(seat)
        If Not game.PlaceBet(seat, animalIndex, amount, currentScore) Then
            RejectBet(seat)
            Return
        End If
        lockedAnimalBySeat(seat) = animalIndex
        lockedAmountBySeat(seat) = amount
        hub.Broadcast("VQR_LOCK:" & seat.ToString() & "|" & animalIndex.ToString() & "|" & amount.ToString())
        RefreshPlayerCards()
    End Sub

    ''' <summary>Bao cho 1 seat biet cuoc vua gui bi tu choi, de ho mo khoa UI dat cuoc lai.</summary>
    Private Sub RejectBet(seat As Integer)
        If seat = 0 Then
            UnlockLocalBetUI()
        Else
            hub.SendToClient(seat, "VQR_BET_NACK:")
        End If
    End Sub

    ''' <summary>Mo lai nut Khoa cuoc tren may nay sau khi cuoc bi Host tu choi (dung cho ca Host va Client).</summary>
    Private Sub UnlockLocalBetUI()
        hasLockedThisRound = False
        If state = RoundState.Betting Then
            btnLockBet.Enabled = True
            nudBet.Enabled = True
        End If
        AppendChat("[He thong] Cuoc vua roi khong hop le, hay dat cuoc lai.")
    End Sub

    Private Sub BtnLockBet_Click(sender As Object, e As EventArgs)
        If state <> RoundState.Betting Then Return
        If hasLockedThisRound Then Return
        If selectedAnimalIndex < 0 Then
            MessageBox.Show("Hay bam chon 1 con vat tren vong quay truoc.")
            Return
        End If
        Dim amount As Long = CLng(nudBet.Value)
        Dim mySeat As Integer = If(isHost, 0, localSeat)
        If mySeat >= 0 AndAlso amount > scoresBySeat(mySeat) Then
            MessageBox.Show("Ban khong du diem de dat cuoc muc nay.")
            Return
        End If
        hasLockedThisRound = True
        btnLockBet.Enabled = False
        nudBet.Enabled = False

        If isHost Then
            ProcessBetFromSeat(0, selectedAnimalIndex, amount)
        Else
            If peer IsNot Nothing AndAlso peer.IsConnected Then
                peer.SendLine("VQR_BET:" & selectedAnimalIndex.ToString() & "|" & amount.ToString())
            End If
        End If
    End Sub

    ' ============================================================
    '  KET QUA
    ' ============================================================
    Private Sub ApplyResult(resultIndex As Integer, entriesRaw As String)
        lastResultIndex = resultIndex
        state = RoundState.ShowingResult
        lblRoundInfo.Text = "Ket qua: " & VongQuayRongGame.Animals(resultIndex).Name &
                             " (x" & VongQuayRongGame.Animals(resultIndex).Multiplier.ToString() & ")"

        If entriesRaw.Trim() <> "" Then
            Dim entries As String() = entriesRaw.Split(";"c)
            Dim en As String
            For Each en In entries
                If en.Trim() = "" Then Continue For
                Dim f As String() = en.Split(","c)
                Dim seat As Integer = Integer.Parse(f(0), CultureInfo.InvariantCulture)
                Dim animal As Integer = Integer.Parse(f(1), CultureInfo.InvariantCulture)
                Dim amount As Long = Long.Parse(f(2), CultureInfo.InvariantCulture)
                Dim won As Boolean = (f(3) = "1")
                Dim payout As Long = Long.Parse(f(4), CultureInfo.InvariantCulture)
                Dim newScore As Long = Long.Parse(f(5), CultureInfo.InvariantCulture)
                scoresBySeat(seat) = newScore

                Dim tag As String = "Player " & (seat + 1).ToString()
                lastRoundHasResult(seat) = True
                lastRoundWonBySeat(seat) = won
                lastRoundPayoutBySeat(seat) = payout
                If won Then
                    AppendChat("[Ket qua] " & tag & " thang " & payout.ToString() & " diem (cuoc " &
                               VongQuayRongGame.Animals(animal).Name & " x" & amount.ToString() & ").")
                Else
                    AppendChat("[Ket qua] " & tag & " thua " & Math.Abs(payout).ToString() & " diem.")
                End If
            Next en
        End If

        If isHost Then
            btnHostAction.Text = "Van moi"
            btnHostAction.Enabled = True
        End If
        RefreshPlayerCards()
    End Sub

    ' ============================================================
    '  HIEU UNG QUAY (dung chung cho ca Host va Client, chi ve hinh)
    ' ============================================================
    Private Sub StartSpinAnimation(targetIndex As Integer)
        state = RoundState.Spinning
        boardPanel.Cursor = Cursors.Default
        spinAnimInProgress = True
        pendingResultIndex = -1
        pendingResultEntries = Nothing
        If isHost Then
            btnHostAction.Enabled = False
        End If
        btnLockBet.Enabled = False
        lblRoundInfo.Text = "Dang quay..."
        lblCountdown.Text = ""

        BuildSpinSequence(targetIndex)
        spinPos = 0
        If spinTimer Is Nothing Then
            spinTimer = New Timer()
            AddHandler spinTimer.Tick, AddressOf SpinTimer_Tick
        End If
        spinTimer.Interval = spinDelays(0)
        spinTimer.Start()
    End Sub

    ''' <summary>Tao chuoi buoc nhay qua tung con vat: quay nhanh nhieu vong roi cham dan
    ''' va dung dung tai targetIndex (giong hieu ung den chay cua may xu / vong quay thuong).</summary>
    Private Sub BuildSpinSequence(targetIndex As Integer)
        spinSequence.Clear()
        spinDelays.Clear()
        Const laps As Integer = 3
        Dim n As Integer = VongQuayRongGame.ANIMAL_COUNT
        Dim totalSteps As Integer = laps * n + targetIndex + 1
        Dim i As Integer
        For i = 0 To totalSteps - 1
            spinSequence.Add(i Mod n)
        Next i
        Dim total As Integer = spinSequence.Count
        For i = 0 To total - 1
            Dim remain As Integer = total - i
            Dim d As Integer
            If remain > n Then
                d = 40
            Else
                Dim frac As Double = (n - remain) / CDbl(n)
                d = 40 + CInt(240.0 * frac)
            End If
            spinDelays.Add(d)
        Next i
    End Sub

    Private Sub SpinTimer_Tick(sender As Object, e As EventArgs)
        highlightIndex = spinSequence(spinPos)
        boardPanel.Invalidate()
        spinPos += 1
        If spinPos >= spinSequence.Count Then
            spinTimer.Stop()
            OnSpinAnimationFinished()
        Else
            spinTimer.Interval = spinDelays(spinPos)
        End If
    End Sub

    Private Sub OnSpinAnimationFinished()
        boardPanel.Invalidate()
        spinAnimInProgress = False
        If pendingResultIndex >= 0 Then
            Dim r As Integer = pendingResultIndex
            Dim ent As String = pendingResultEntries
            pendingResultIndex = -1
            pendingResultEntries = Nothing
            ApplyResult(r, ent)
        End If
        ' Neu ket qua tu mang chua toi kip (hiem, do do tre mang), cu giu nguyen man
        ' hinh "Dang quay..." va cho VQR_RESULT den, luc do QueueOrApplyResult se tu
        ' goi ApplyResult ngay vi spinAnimInProgress da False.
    End Sub

    ''' <summary>Nhan ket qua tu mang (hoac tu chinh Host): neu vong quay tren man hinh
    ''' nay dang chay thi giu lai, cho quay xong moi cong bo - de tao cam giac hoi hop.</summary>
    Private Sub QueueOrApplyResult(resultIndex As Integer, entries As String)
        If spinAnimInProgress Then
            pendingResultIndex = resultIndex
            pendingResultEntries = entries
        Else
            ApplyResult(resultIndex, entries)
        End If
    End Sub

    ' ============================================================
    '  GAME PANEL (Board + dieu khien dat cuoc + the nguoi choi)
    ' ============================================================
    Private Sub ShowGamePanel()
        If pnlGame IsNot Nothing Then Return ' da dung
        pnlConnect.Visible = False

        pnlGame = New Panel()
        pnlGame.Dock = DockStyle.Fill
        pnlGame.BackColor = Color.FromArgb(20, 24, 30)

        boardPanel = New Panel()
        boardPanel.Location = New Point(20, 20)
        boardPanel.Size = New Size(BOARD_W, BOARD_H)
        boardPanel.BackColor = Color.FromArgb(10, 40, 30)
        boardPanel.BorderStyle = BorderStyle.FixedSingle
        AddHandler boardPanel.Paint, AddressOf BoardPanel_Paint
        AddHandler boardPanel.MouseClick, AddressOf BoardPanel_MouseClick
        pnlGame.Controls.Add(boardPanel)
        RecomputeAnimalCenters()

        lblRoundInfo = New Label()
        lblRoundInfo.Location = New Point(20, BOARD_H + 30) : lblRoundInfo.AutoSize = True
        lblRoundInfo.ForeColor = Color.White
        lblRoundInfo.Font = New Font("Segoe UI", 10.0!, FontStyle.Bold)
        lblRoundInfo.Text = "Cho Host bat dau van moi..."
        pnlGame.Controls.Add(lblRoundInfo)

        lblCountdown = New Label()
        lblCountdown.Location = New Point(20, BOARD_H + 55) : lblCountdown.AutoSize = True
        lblCountdown.ForeColor = Color.Gold
        lblCountdown.Font = New Font("Segoe UI", 10.0!)
        pnlGame.Controls.Add(lblCountdown)

        Dim lblBetCap As New Label()
        lblBetCap.Text = "Diem cuoc (" & VongQuayRongGame.MIN_BET.ToString() & "-" & VongQuayRongGame.MAX_BET.ToString() & "):"
        lblBetCap.AutoSize = True
        lblBetCap.ForeColor = Color.White
        lblBetCap.Font = New Font("Segoe UI", 9.5!, FontStyle.Bold)
        lblBetCap.Location = New Point(20, BOARD_H + 92)
        pnlGame.Controls.Add(lblBetCap)

        nudBet = New NumericUpDown()
        nudBet.Location = New Point(175, BOARD_H + 86)
        nudBet.Size = New Size(80, 26)
        nudBet.Font = New Font("Segoe UI", 10.0!, FontStyle.Bold)
        nudBet.BackColor = Color.White
        nudBet.ForeColor = Color.Black
        nudBet.BorderStyle = BorderStyle.FixedSingle
        nudBet.TextAlign = HorizontalAlignment.Center
        nudBet.Minimum = CDec(VongQuayRongGame.MIN_BET)
        nudBet.Maximum = CDec(VongQuayRongGame.MAX_BET)
        nudBet.Increment = 10
        nudBet.Value = CDec(VongQuayRongGame.MIN_BET)
        nudBet.Enabled = False
        pnlGame.Controls.Add(nudBet)

        btnLockBet = New Button()
        btnLockBet.Text = "Khoa cuoc"
        btnLockBet.Location = New Point(270, BOARD_H + 84) : btnLockBet.Size = New Size(120, 30)
        btnLockBet.Font = New Font("Segoe UI", 9.5!, FontStyle.Bold)
        btnLockBet.FlatStyle = FlatStyle.Flat
        btnLockBet.FlatAppearance.BorderSize = 0
        btnLockBet.BackColor = Color.FromArgb(46, 160, 67)
        btnLockBet.ForeColor = Color.White
        btnLockBet.Enabled = False
        AddHandler btnLockBet.Click, AddressOf BtnLockBet_Click
        pnlGame.Controls.Add(btnLockBet)

        btnHostAction = New Button()
        btnHostAction.Text = "Bat dau van moi"
        btnHostAction.Location = New Point(405, BOARD_H + 84) : btnHostAction.Size = New Size(160, 30)
        btnHostAction.Font = New Font("Segoe UI", 9.5!, FontStyle.Bold)
        btnHostAction.FlatStyle = FlatStyle.Flat
        btnHostAction.FlatAppearance.BorderSize = 0
        btnHostAction.BackColor = Color.FromArgb(41, 121, 255)
        btnHostAction.ForeColor = Color.White
        btnHostAction.Visible = isHost
        AddHandler btnHostAction.Click, AddressOf BtnHostAction_Click
        pnlGame.Controls.Add(btnHostAction)

        Dim sideX As Integer = BOARD_W + 40
        Dim p As Integer
        For p = 0 To 3
            pnlPlayers(p) = BuildPlayerCard(p, New Point(sideX, 20 + p * 80), 300)
            pnlGame.Controls.Add(pnlPlayers(p))
        Next p

        BuildChatPanel(sideX, 300, 20 + 4 * 80 + 10, 700 - (20 + 4 * 80 + 10) - 20)

        Me.Controls.Add(pnlGame)
        RefreshPlayerCards()
    End Sub

    Private Function BuildPlayerCard(player As Integer, loc As Point, w As Integer) As Panel
        Dim card As New Panel()
        card.Location = loc : card.Size = New Size(w, 80)
        card.BackColor = Color.White
        card.BorderStyle = BorderStyle.FixedSingle

        Dim bar As New Panel()
        bar.Location = New Point(0, 0) : bar.Size = New Size(6, 80)
        bar.BackColor = PlayerColor(player)
        card.Controls.Add(bar)

        Dim lblTitle As New Label()
        lblTitle.Name = "title"
        lblTitle.Font = New Font("Segoe UI", 9.5!, FontStyle.Bold)
        lblTitle.ForeColor = PlayerColor(player)
        lblTitle.Location = New Point(16, 4) : lblTitle.AutoSize = True
        card.Controls.Add(lblTitle)

        lblCardStatus(player) = New Label()
        lblCardStatus(player).Font = New Font("Segoe UI", 9.0!)
        lblCardStatus(player).ForeColor = Color.DimGray
        lblCardStatus(player).Location = New Point(16, 22) : lblCardStatus(player).AutoSize = True
        card.Controls.Add(lblCardStatus(player))

        lblCardStats(player) = New Label()
        lblCardStats(player).Font = New Font("Segoe UI", 8.0!, FontStyle.Bold)
        lblCardStats(player).ForeColor = Color.FromArgb(133, 77, 14)      ' nau dam - de doc
        lblCardStats(player).BackColor = Color.FromArgb(255, 236, 179)   ' vang nhat - noi bat lam badge
        lblCardStats(player).Padding = New Padding(6, 2, 6, 2)
        lblCardStats(player).Location = New Point(16, 38) : lblCardStats(player).AutoSize = True
        lblCardStats(player).Visible = False
        card.Controls.Add(lblCardStats(player))

        lblCardResult(player) = New Label()
        lblCardResult(player).Font = New Font("Segoe UI", 8.5!, FontStyle.Bold)
        lblCardResult(player).ForeColor = Color.Gray
        lblCardResult(player).Text = ""
        lblCardResult(player).Location = New Point(16, 60) : lblCardResult(player).AutoSize = True
        card.Controls.Add(lblCardResult(player))

        Return card
    End Function

    Private Sub RefreshPlayerCards()
        Dim p As Integer
        For p = 0 To 3
            If pnlPlayers(p) Is Nothing Then Continue For
            Dim titleLbl As Label = CType(pnlPlayers(p).Controls("title"), Label)
            Dim suffix As String = ""
            If p = localSeat Then suffix = " (Ban)"
            titleLbl.Text = "Player " & (p + 1).ToString() & " - " & playerNames(p) & suffix

            If p = 0 OrElse playerConnected(p) Then
                lblCardStatus(p).Text = "Diem: " & scoresBySeat(p).ToString()
            Else
                lblCardStatus(p).Text = "(trong)"
            End If

            If lockedAnimalBySeat(p) >= 0 Then
                Dim a As VongQuayRongGame.AnimalInfo = VongQuayRongGame.Animals(lockedAnimalBySeat(p))
                lblCardStats(p).Text = "DA KHOA: " & a.Name & " x" & a.Multiplier.ToString() &
                                        "  (" & lockedAmountBySeat(p).ToString() & " diem)"
                lblCardStats(p).Visible = True
            Else
                lblCardStats(p).Text = ""
                lblCardStats(p).Visible = False
            End If

            If lastRoundHasResult(p) Then
                If lastRoundWonBySeat(p) Then
                    lblCardResult(p).Text = "Vua roi: THANG +" & lastRoundPayoutBySeat(p).ToString() & " diem"
                    lblCardResult(p).ForeColor = Color.FromArgb(30, 140, 40)
                    pnlPlayers(p).BackColor = Color.FromArgb(224, 250, 224)
                Else
                    lblCardResult(p).Text = "Vua roi: THUA " & Math.Abs(lastRoundPayoutBySeat(p)).ToString() & " diem"
                    lblCardResult(p).ForeColor = Color.FromArgb(190, 40, 40)
                    pnlPlayers(p).BackColor = Color.FromArgb(255, 226, 226)
                End If
            Else
                lblCardResult(p).Text = ""
                pnlPlayers(p).BackColor = Color.White
            End If
        Next p
    End Sub

    ' ============================================================
    '  VE VONG QUAY
    ' ============================================================
    Private Sub RecomputeAnimalCenters()
        boardRect = New Rectangle(60, 36, BOARD_W - 120, BOARD_H - 90)
        Dim n As Integer = VongQuayRongGame.ANIMAL_COUNT
        Dim i As Integer
        For i = 0 To n - 1
            Dim t As Double = (i + 0.5) / n
            animalCenters(i) = PerimeterPoint(t, boardRect)
        Next i
    End Sub

    ''' <summary>Tra ve 1 diem tren vien hinh chu nhat, di theo chieu kim dong ho bat dau tu
    ''' goc tren-trai, voi t trong doan [0,1) la ty le quang duong da di so voi chu vi.</summary>
    Private Function PerimeterPoint(t As Double, rect As Rectangle) As PointF
        Dim w As Double = rect.Width
        Dim h As Double = rect.Height
        Dim perim As Double = 2.0 * (w + h)
        Dim dist As Double = t * perim

        If dist <= w Then
            Return New PointF(CSng(rect.Left + dist), CSng(rect.Top))
        End If
        dist -= w
        If dist <= h Then
            Return New PointF(CSng(rect.Right), CSng(rect.Top + dist))
        End If
        dist -= h
        If dist <= w Then
            Return New PointF(CSng(rect.Right - dist), CSng(rect.Bottom))
        End If
        dist -= w
        Return New PointF(CSng(rect.Left), CSng(rect.Bottom - dist))
    End Function

    Private Sub BoardPanel_Paint(sender As Object, e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(Color.FromArgb(10, 40, 30))

        Using framePen As New Pen(Color.FromArgb(200, 170, 60), 3)
            g.DrawRectangle(framePen, boardRect)
        End Using

        Dim centerPt As New PointF(boardPanel.Width / 2.0F, boardPanel.Height / 2.0F)

        ' Tia sang toi con vat dang sang (khi dang quay hoac vua dung)
        If highlightIndex >= 0 Then
            Using beamPen As New Pen(Color.FromArgb(150, 255, 230, 80), 3)
                g.DrawLine(beamPen, centerPt, animalCenters(highlightIndex))
            End Using
        End If

        ' "Dau rong" o giua - dung sprite neu co, khong thi fallback ve tay
        Dim dr As Single = 30
        If dragonImage IsNot Nothing Then
            DrawSpriteCircular(g, dragonImage, centerPt, dr, Color.FromArgb(255, 220, 130), 2)
        Else
            Using dragonBrush As New SolidBrush(Color.FromArgb(220, 170, 40))
                g.FillEllipse(dragonBrush, centerPt.X - dr, centerPt.Y - dr, dr * 2, dr * 2)
            End Using
            Using dragonPen As New Pen(Color.FromArgb(255, 220, 130), 2)
                g.DrawEllipse(dragonPen, centerPt.X - dr, centerPt.Y - dr, dr * 2, dr * 2)
            End Using
            DrawCenteredString(g, "RONG", centerPt, New Font("Segoe UI", 8.0!, FontStyle.Bold), Color.White)
        End If

        ' 12 con vat
        Dim i As Integer
        For i = 0 To VongQuayRongGame.ANIMAL_COUNT - 1
            Dim a As VongQuayRongGame.AnimalInfo = VongQuayRongGame.Animals(i)
            Dim c As PointF = animalCenters(i)

            Dim r As Single = CELL_RADIUS
            If i = highlightIndex Then r = CELL_RADIUS + 4

            Dim ringColor As Color
            Dim ringWidth As Integer
            If i = highlightIndex Then
                ringColor = Color.Gold : ringWidth = 3
            ElseIf i = selectedAnimalIndex Then
                ringColor = PlayerColor(Math.Max(localSeat, 0)) : ringWidth = 3
            ElseIf i = lastResultIndex AndAlso state = RoundState.ShowingResult Then
                ringColor = Color.LimeGreen : ringWidth = 3
            Else
                ringColor = Color.White : ringWidth = 1
            End If

            If animalImages(i) IsNot Nothing Then
                DrawSpriteCircular(g, animalImages(i), c, r, ringColor, ringWidth)
            Else
                Using body As New SolidBrush(a.BodyColor)
                    g.FillEllipse(body, c.X - r, c.Y - r, r * 2, r * 2)
                End Using
                Using borderPen As New Pen(ringColor, ringWidth)
                    g.DrawEllipse(borderPen, c.X - r, c.Y - r, r * 2, r * 2)
                End Using
                DrawCenteredString(g, a.Name, New PointF(c.X, c.Y - 4), New Font("Segoe UI", 7.0!, FontStyle.Bold), Color.White)
            End If

            DrawCenteredString(g, "x" & a.Multiplier.ToString(), New PointF(c.X, c.Y + r + 9), New Font("Segoe UI", 7.5!, FontStyle.Bold), Color.Yellow)

            ' Cham mau nho danh dau seat nao da khoa cuoc vao con nay
            Dim seatIdx As Integer
            Dim markOffset As Integer = 0
            For seatIdx = 0 To 3
                If lockedAnimalBySeat(seatIdx) = i Then
                    Using markBrush As New SolidBrush(PlayerColor(seatIdx))
                        g.FillEllipse(markBrush, c.X - r + markOffset, c.Y - r - 2, 8, 8)
                    End Using
                    markOffset += 10
                End If
            Next seatIdx
        Next i
    End Sub

    ''' <summary>Ve 1 anh sprite duoc cat tron (clip hinh tron) tam c, ban kinh r, kem vien mau ringColor.
    ''' Dung chung cho ca 12 con vat va dau rong.</summary>
    Private Sub DrawSpriteCircular(g As Graphics, img As Image, c As PointF, r As Single, ringColor As Color, ringWidth As Integer)
        Dim rectF As New RectangleF(c.X - r, c.Y - r, r * 2, r * 2)
        Dim path As New GraphicsPath()
        path.AddEllipse(rectF)
        Dim oldClip As Region = g.Clip
        g.SetClip(path, CombineMode.Intersect)
        g.DrawImage(img, rectF.X, rectF.Y, rectF.Width, rectF.Height)
        g.Clip = oldClip
        path.Dispose()
        Using borderPen As New Pen(ringColor, ringWidth)
            g.DrawEllipse(borderPen, rectF)
        End Using
    End Sub

    Private Sub DrawCenteredString(g As Graphics, text As String, center As PointF, font As Font, color As Color)
        Dim sz As SizeF = g.MeasureString(text, font)
        Using b As New SolidBrush(color)
            g.DrawString(text, font, b, center.X - sz.Width / 2.0F, center.Y - sz.Height / 2.0F)
        End Using
    End Sub

    ''' <summary>Hien gop y mau cam tam thoi khi nguoi choi bam vao vong quay nhung chua den luot
    ''' dat cuoc (vi du: Host chua bam "Bat dau van moi"), de tranh cam giac ung dung bi "cung do".</summary>
    Private Sub ShowRoundHint()
        Dim msg As String
        If state = RoundState.Idle AndAlso isHost Then
            msg = ">> Ban can bam nut ""Bat dau van moi"" truoc khi chon con vat!"
        ElseIf state = RoundState.Idle Then
            msg = ">> Dang cho Host bam ""Bat dau van moi""..."
        ElseIf state = RoundState.Spinning Then
            msg = ">> Dang quay, vui long doi ket qua..."
        ElseIf hasLockedThisRound Then
            msg = ">> Ban da khoa cuoc van nay roi, doi ket qua nhe."
        Else
            msg = ">> Chua the chon luc nay."
        End If

        lblRoundInfo.Text = msg
        lblRoundInfo.ForeColor = Color.Orange

        If hintTimer Is Nothing Then
            hintTimer = New Timer()
            hintTimer.Interval = 1500
            AddHandler hintTimer.Tick, AddressOf HintTimer_Tick
        End If
        hintTimer.Stop()
        hintTimer.Start()
    End Sub

    ''' <summary>Sau khi het thoi gian hien gop y, tra lblRoundInfo ve mau/noi dung dung voi trang thai hien tai.</summary>
    Private Sub HintTimer_Tick(sender As Object, e As EventArgs)
        hintTimer.Stop()
        lblRoundInfo.ForeColor = Color.White
        Select Case state
            Case RoundState.Idle
                lblRoundInfo.Text = "Cho Host bat dau van moi..."
            Case RoundState.Betting
                lblRoundInfo.Text = "Van " & game.CurrentRoundNo.ToString() & " - hay chon 1 con vat va khoa cuoc!"
            Case RoundState.Spinning
                lblRoundInfo.Text = "Dang quay..."
        End Select
    End Sub

    Private Sub BoardPanel_MouseClick(sender As Object, e As MouseEventArgs)
        If state <> RoundState.Betting Then
            ShowRoundHint()
            Return
        End If
        If hasLockedThisRound Then Return

        Dim best As Integer = -1
        Dim bestDist As Double = Double.MaxValue
        Dim i As Integer
        For i = 0 To VongQuayRongGame.ANIMAL_COUNT - 1
            Dim c As PointF = animalCenters(i)
            Dim dx As Double = c.X - e.X
            Dim dy As Double = c.Y - e.Y
            Dim d As Double = Math.Sqrt(dx * dx + dy * dy)
            If d < bestDist Then
                bestDist = d
                best = i
            End If
        Next i

        If best >= 0 AndAlso bestDist <= CELL_RADIUS + 10 Then
            selectedAnimalIndex = best
            boardPanel.Invalidate()
        End If
    End Sub

    ' ============================================================
    '  CHAT (giu nguyen giao thuc CHAT:<tag>:<msg> nhu cac project truoc)
    ' ============================================================
    Private Sub BuildChatPanel(x As Integer, w As Integer, y As Integer, h As Integer)
        pnlChat = New Panel()
        pnlChat.Location = New Point(x, y)
        pnlChat.Size = New Size(w, h)

        lstChat = New ListBox()
        lstChat.Location = New Point(0, 0)
        lstChat.Size = New Size(w, h - 30)
        pnlChat.Controls.Add(lstChat)

        txtChatInput = New TextBox()
        txtChatInput.Location = New Point(0, h - 26)
        txtChatInput.Size = New Size(w - 55, 24)
        AddHandler txtChatInput.KeyDown, Sub(s As Object, ev As KeyEventArgs)
            If ev.KeyCode = Keys.Enter Then
                BtnSend_Click(s, EventArgs.Empty)
                ev.Handled = True
                ev.SuppressKeyPress = True
            End If
        End Sub
        pnlChat.Controls.Add(txtChatInput)

        btnSend = New Button()
        btnSend.Text = "Gui"
        btnSend.Location = New Point(w - 50, h - 27)
        btnSend.Size = New Size(50, 26)
        AddHandler btnSend.Click, AddressOf BtnSend_Click
        pnlChat.Controls.Add(btnSend)

        pnlGame.Controls.Add(pnlChat)
    End Sub

    Private Sub BtnSend_Click(sender As Object, e As EventArgs)
        If txtChatInput.Text.Trim() = "" Then Return
        If localSeat < 0 Then Return
        Dim tag As String = "Player " & (localSeat + 1).ToString()
        Dim msg As String = txtChatInput.Text.Trim()
        AppendChat(tag & ": " & msg)

        If isHost Then
            hub.Broadcast("CHAT:" & tag & ":" & msg)
        Else
            If peer IsNot Nothing AndAlso peer.IsConnected Then peer.SendLine("CHAT:" & tag & ":" & msg)
        End If

        txtChatInput.Text = ""
        txtChatInput.Focus()
    End Sub

    Private Sub AppendChat(msg As String)
        If lstChat Is Nothing Then Return
        lstChat.Items.Add(msg)
        lstChat.TopIndex = Math.Max(0, lstChat.Items.Count - 1)
    End Sub

End Class
