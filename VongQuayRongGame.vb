Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Collections.Generic

''' <summary>
''' Logic thuan tuy (khong dinh UI) cho game "Vong Quay Rong":
''' - 12 con vat co dinh quanh vien, moi con co he so nhan (x5/x6/x9).
''' - Moi van: nguoi choi dat cuoc (chon 1 con vat + so diem cuoc).
''' - Host quay ngau nhien co trong so (con vat he so cao thi ti le ra thap hon,
'''   giu ky vong hoan tra (RTP) bang nhau giua cac con vat - xem GetWeight()).
''' - Ai chon trung con vat quay trung thi duoc CUOC * HeSo diem, sai thi mat CUOC diem.
''' Class nay CHI duoc Host dung de tinh toan (RNG + tinh diem). Client chi ve lai
''' ket qua Host gui ve, khong tu quay.
''' </summary>
Public Class VongQuayRongGame

    Public Const ANIMAL_COUNT As Integer = 12

    ''' <summary>Chi so dai dien cho o "No Hu" (Jackpot) - o thu 13 tren vong quay, ngoai 12 con vat.</summary>
    Public Const JACKPOT_INDEX As Integer = ANIMAL_COUNT

    ''' <summary>Tong so o tren vong quay: 12 con vat + 1 o No Hu.</summary>
    Public Const TOTAL_SLOTS As Integer = ANIMAL_COUNT + 1

    ''' <summary>Trong so quay cua o No Hu - rat thap so voi tung con vat, de No Hu la su kien hiem.</summary>
    Public Const JACKPOT_WEIGHT As Integer = 6

    ''' <summary>Quy Jackpot khoi tao / muc san sau khi vua "vo hu".</summary>
    Public Const JACKPOT_SEED As Long = 500

    ''' <summary>Ty le trich them (tien nha cai gop, KHONG tru diem nguoi choi) tu moi cuoc
    ''' de nuoi quy Jackpot moi van.</summary>
    Public Const JACKPOT_CONTRIBUTION_RATE As Double = 0.1

    ''' <summary>Diem khoi dau cua moi nguoi choi khi vao phong.</summary>
    Public Const STARTING_SCORE As Long = 1000

    ''' <summary>Muc dat cuoc toi thieu / toi da cho moi van (ap dung ca cho o No Hu).</summary>
    Public Const MIN_BET As Long = 50
    Public Const MAX_BET As Long = 200

    ''' <summary>Thong tin 1 con vat tren vong quay.</summary>
    Public Class AnimalInfo
        Public ReadOnly Name As String
        Public ReadOnly Multiplier As Integer   ' he so thuong: x5, x6, x9...
        Public ReadOnly WeightRandom As Integer ' trong so quay (cao = de ra hon)
        Public ReadOnly BodyColor As Color
        Public ReadOnly ImageFile As String     ' ten file anh trong thu muc Assets\ (co the rong)

        Public Sub New(name_ As String, multiplier_ As Integer, weight_ As Integer, color_ As Color, imageFile_ As String)
            Name = name_
            Multiplier = multiplier_
            WeightRandom = weight_
            BodyColor = color_
            ImageFile = imageFile_
        End Sub
    End Class

    ''' <summary>1 luot dat cuoc cua 1 seat trong van hien tai.</summary>
    Public Class BetInfo
        Public Seat As Integer
        Public AnimalIndex As Integer
        Public Amount As Long
        Public Locked As Boolean = False
    End Class

    ''' <summary>Ket qua tinh thuong/thua cua 1 seat sau khi co ket qua quay.</summary>
    Public Class RoundOutcome
        Public Seat As Integer
        Public AnimalIndex As Integer
        Public Amount As Long
        Public Won As Boolean
        Public Payout As Long      ' duong = duoc them, am = bi tru
        Public NewScore As Long
        Public IsJackpot As Boolean = False ' True neu van nay ket qua roi vao o No Hu
        Public JackpotShare As Long = 0     ' phan quy Jackpot nhan duoc (chi > 0 khi thang o No Hu)
    End Class

    Public Shared ReadOnly Animals() As AnimalInfo = BuildAnimals()

    ' ------- Trang thai van dau hien tai (chi Host dung de xu ly) -------
    Public CurrentRoundNo As Integer = 0
    Public CurrentBets As New Dictionary(Of Integer, BetInfo)  ' seat -> cuoc, reset moi van

    ''' <summary>Quy Jackpot hien tai (chi Host theo doi). Bat dau tu JACKPOT_SEED, tang dan
    ''' moi van dat cuoc, "vo" het khi co nguoi trung o No Hu (roi quay ve muc san).</summary>
    Public JackpotPool As Long = JACKPOT_SEED

    Private rngInstance As New Random()

    Private Shared Function BuildAnimals() As AnimalInfo()
        ' Muc thuong chia lam 3 bac: x5 (thuong), x6 (kha), x9 (hiem).
        ' Trong so tinh theo 90 / HeSo de RTP (ky vong hoan tra) bang nhau ~52% cho moi con,
        ' Cong co the chinh BASE_WEIGHT o day de tang/giam ty le nha cai.
        Const BASE_WEIGHT As Integer = 90
        Dim list As New List(Of AnimalInfo)
        list.Add(New AnimalInfo("Khi", 6, BASE_WEIGHT \ 6, Color.FromArgb(230, 150, 60), "khi.png"))
        list.Add(New AnimalInfo("Tho", 5, BASE_WEIGHT \ 5, Color.FromArgb(235, 235, 235), "tho.png"))
        list.Add(New AnimalInfo("Gau truc", 5, BASE_WEIGHT \ 5, Color.FromArgb(40, 40, 40), "gautruc.png"))
        list.Add(New AnimalInfo("Su tu", 9, BASE_WEIGHT \ 9, Color.FromArgb(220, 160, 40), "sutu.png"))
        list.Add(New AnimalInfo("Voi", 9, BASE_WEIGHT \ 9, Color.FromArgb(150, 150, 155), "voi.png"))
        list.Add(New AnimalInfo("Doi", 6, BASE_WEIGHT \ 6, Color.FromArgb(70, 60, 90), "doi.png"))
        list.Add(New AnimalInfo("Ngua", 5, BASE_WEIGHT \ 5, Color.FromArgb(140, 90, 50), "ngua.png"))
        list.Add(New AnimalInfo("Cao", 6, BASE_WEIGHT \ 6, Color.FromArgb(210, 90, 40), "cao.png"))
        list.Add(New AnimalInfo("Huou", 9, BASE_WEIGHT \ 9, Color.FromArgb(160, 110, 70), "huou.png"))
        list.Add(New AnimalInfo("Ga trong", 5, BASE_WEIGHT \ 5, Color.FromArgb(200, 40, 50), "gatrong.png"))
        list.Add(New AnimalInfo("Cho", 6, BASE_WEIGHT \ 6, Color.FromArgb(170, 120, 70), "cho.png"))
        list.Add(New AnimalInfo("Meo", 5, BASE_WEIGHT \ 5, Color.FromArgb(120, 120, 130), "meo.png"))
        Return list.ToArray()
    End Function

    ''' <summary>Bat dau van moi: tang so van, xoa het cuoc cu.</summary>
    Public Sub StartNewRound()
        CurrentRoundNo += 1
        CurrentBets.Clear()
    End Sub

    ''' <summary>Ghi nhan cuoc cua 1 seat. Tra False neu du lieu khong hop le (sai con vat,
    ''' cuoc ngoai khoang MIN_BET..MAX_BET, hoac cuoc vuot qua diem hien co).</summary>
    Public Function PlaceBet(seat As Integer, animalIndex As Integer, amount As Long, currentScore As Long) As Boolean
        If animalIndex < 0 OrElse animalIndex >= TOTAL_SLOTS Then Return False
        If amount < MIN_BET OrElse amount > MAX_BET Then Return False
        If amount > currentScore Then Return False
        Dim b As New BetInfo()
        b.Seat = seat
        b.AnimalIndex = animalIndex
        b.Amount = amount
        b.Locked = True
        CurrentBets(seat) = b
        Return True
    End Function

    Public Function HasBet(seat As Integer) As Boolean
        Return CurrentBets.ContainsKey(seat)
    End Function

    ''' <summary>Host quay: chon 1 index 0..11 theo trong so WeightRandom cua tung con vat, hoac
    ''' JACKPOT_INDEX (12) theo trong so JACKPOT_WEIGHT rat thap (o No Hu, su kien hiem).</summary>
    Public Function SpinResult() As Integer
        Dim totalWeight As Integer = 0
        Dim i As Integer
        For i = 0 To ANIMAL_COUNT - 1
            totalWeight += Animals(i).WeightRandom
        Next i
        totalWeight += JACKPOT_WEIGHT

        Dim roll As Integer = rngInstance.Next(0, totalWeight)
        Dim acc As Integer = 0
        For i = 0 To ANIMAL_COUNT - 1
            acc += Animals(i).WeightRandom
            If roll < acc Then Return i
        Next i
        ' Phan trong so con lai (JACKPOT_WEIGHT) ung voi o No Hu
        Return JACKPOT_INDEX
    End Function

    ''' <summary>Tinh thuong/thua cho tat ca seat da dat cuoc trong van, dua vao ket qua quay.
    ''' scoresBySeat la diem HIEN TAI cua tung seat (se duoc cong don va cap nhat trong outcome).
    ''' Neu resultIndex = JACKPOT_INDEX (o No Hu): nhung ai dat cuoc vao o No Hu se chia deu
    ''' nhau quy JackpotPool hien co (roi quy tro ve muc JACKPOT_SEED); ai dat cuoc con vat
    ''' khac van mat cuoc binh thuong vi khong trung. Moi van (bat ke ket qua ra sao), 1 phan
    ''' nho (JACKPOT_CONTRIBUTION_RATE) cua tung cuoc duoc gop them vao quy Jackpot cho van sau -
    ''' day la tien "nha cai" bu them, KHONG tru vao diem cua nguoi choi.</summary>
    Public Function ComputePayouts(resultIndex As Integer, scoresBySeat As Dictionary(Of Integer, Long)) As List(Of RoundOutcome)
        ' Gop quy Jackpot tu tat ca cuoc van nay (khong anh huong diem nguoi choi)
        For Each kvSeed As KeyValuePair(Of Integer, BetInfo) In CurrentBets
            Dim contrib As Long = CLng(Math.Floor(CDbl(kvSeed.Value.Amount) * JACKPOT_CONTRIBUTION_RATE))
            If contrib > 0 Then JackpotPool += contrib
        Next kvSeed

        Dim isJackpotRound As Boolean = (resultIndex = JACKPOT_INDEX)

        ' Neu la van No Hu: tim nhung ai dat cuoc dung o No Hu de chia quy
        Dim jackpotWinnerSeats As New List(Of Integer)
        If isJackpotRound Then
            For Each kvFind As KeyValuePair(Of Integer, BetInfo) In CurrentBets
                If kvFind.Value.AnimalIndex = JACKPOT_INDEX Then jackpotWinnerSeats.Add(kvFind.Key)
            Next kvFind
        End If

        Dim sharePerWinner As Long = 0
        Dim poolPaidOut As Long = 0
        If jackpotWinnerSeats.Count > 0 Then
            sharePerWinner = JackpotPool \ CLng(jackpotWinnerSeats.Count)
            poolPaidOut = sharePerWinner * CLng(jackpotWinnerSeats.Count)
        End If

        Dim results As New List(Of RoundOutcome)
        For Each kv As KeyValuePair(Of Integer, BetInfo) In CurrentBets
            Dim b As BetInfo = kv.Value
            Dim outcome As New RoundOutcome()
            outcome.Seat = b.Seat
            outcome.AnimalIndex = b.AnimalIndex
            outcome.Amount = b.Amount
            outcome.IsJackpot = isJackpotRound

            If b.AnimalIndex = resultIndex Then
                outcome.Won = True
                If isJackpotRound Then
                    outcome.Payout = sharePerWinner
                    outcome.JackpotShare = sharePerWinner
                Else
                    outcome.Payout = b.Amount * CLng(Animals(resultIndex).Multiplier)
                End If
            Else
                outcome.Won = False
                outcome.Payout = -b.Amount
            End If

            Dim oldScore As Long = 0
            If scoresBySeat.ContainsKey(b.Seat) Then oldScore = scoresBySeat(b.Seat)
            Dim newScore As Long = oldScore + outcome.Payout
            scoresBySeat(b.Seat) = newScore
            outcome.NewScore = newScore

            results.Add(outcome)
        Next kv

        ' Vo hu: tru quy da chia ra, roi quay ve muc san toi thieu
        If jackpotWinnerSeats.Count > 0 Then
            JackpotPool -= poolPaidOut
            If JackpotPool < JACKPOT_SEED Then JackpotPool = JACKPOT_SEED
        End If

        Return results
    End Function

End Class
