using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace MQTTClient
{
    public class NullableUIntToStringConverter : MarkupExtension, IValueConverter
    {
        #region Overrides of MarkupExtension

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }

        #endregion

        #region Implementation of IValueConverter

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (value is int num)
            {
                return num.ToString();
            }

            throw new NotSupportedException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var str = (string)value;
            if (string.IsNullOrEmpty(str))
            {
                return null;
            }
            return uint.Parse(str);
        }

        #endregion
    }

    public class NullableUIntFieldValidation : ValidationRule
    {
        public int MinValue { get; set; } = 0;
        public int MaxValue { get; set; } = int.MaxValue;
        private string invalidInput => $"Not an integer value in {MinValue} to {MaxValue} range!";

        #region Overrides of ValidationRule

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (value == null)
            {
                return new ValidationResult(true, null);
            }
            var str = (string)value;
            if (string.IsNullOrEmpty(str))
            {
                return new ValidationResult(true, null);
            }

            if (uint.TryParse(str, out var intVal) && intVal >= MinValue && intVal <= MaxValue)
            {
                return new ValidationResult(true, null);
            }

            return new ValidationResult(false, invalidInput);
        }

        #endregion
    }
}