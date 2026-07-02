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

    ''' <summary>Diem khoi dau cua moi nguoi choi khi vao phong.</summary>
    Public Const STARTING_SCORE As Long = 1000

    ''' <summary>Muc dat cuoc toi thieu / toi da cho moi van.</summary>
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
    End Class

    Public Shared ReadOnly Animals() As AnimalInfo = BuildAnimals()

    ' ------- Trang thai van dau hien tai (chi Host dung de xu ly) -------
    Public CurrentRoundNo As Integer = 0
    Public CurrentBets As New Dictionary(Of Integer, BetInfo)  ' seat -> cuoc, reset moi van

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
        If animalIndex < 0 OrElse animalIndex >= ANIMAL_COUNT Then Return False
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

    ''' <summary>Host quay: chon 1 index 0..11 theo trong so WeightRandom cua tung con vat.</summary>
    Public Function SpinResult() As Integer
        Dim totalWeight As Integer = 0
        Dim i As Integer
        For i = 0 To ANIMAL_COUNT - 1
            totalWeight += Animals(i).WeightRandom
        Next i
        Dim roll As Integer = rngInstance.Next(0, totalWeight)
        Dim acc As Integer = 0
        For i = 0 To ANIMAL_COUNT - 1
            acc += Animals(i).WeightRandom
            If roll < acc Then Return i
        Next i
        Return ANIMAL_COUNT - 1
    End Function

    ''' <summary>Tinh thuong/thua cho tat ca seat da dat cuoc trong van, dua vao ket qua quay.
    ''' scoresBySeat la diem HIEN TAI cua tung seat (se duoc cong don va cap nhat trong outcome).</summary>
    Public Function ComputePayouts(resultIndex As Integer, scoresBySeat As Dictionary(Of Integer, Long)) As List(Of RoundOutcome)
        Dim results As New List(Of RoundOutcome)
        For Each kv As KeyValuePair(Of Integer, BetInfo) In CurrentBets
            Dim b As BetInfo = kv.Value
            Dim outcome As New RoundOutcome()
            outcome.Seat = b.Seat
            outcome.AnimalIndex = b.AnimalIndex
            outcome.Amount = b.Amount

            If b.AnimalIndex = resultIndex Then
                outcome.Won = True
                outcome.Payout = b.Amount * CLng(Animals(resultIndex).Multiplier)
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
        Return results
    End Function

End Class
