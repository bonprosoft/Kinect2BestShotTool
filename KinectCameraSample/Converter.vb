<ValueConversion(GetType(Boolean), GetType(SolidColorBrush))>
Public Class BoolToSolidColorBrushConverter
    Implements IValueConverter

    Public Function Convert(value As Object, targetType As Type, parameter As Object, culture As Globalization.CultureInfo) As Object Implements IValueConverter.Convert
        Dim val = DirectCast(value, Boolean)
        If val Then
            Return New SolidColorBrush(Colors.Green)
        Else
            Return New SolidColorBrush(Colors.Magenta)
        End If
    End Function

    Public Function ConvertBack(value As Object, targetType As Type, parameter As Object, culture As Globalization.CultureInfo) As Object Implements IValueConverter.ConvertBack
        Throw New NotImplementedException
    End Function
End Class
