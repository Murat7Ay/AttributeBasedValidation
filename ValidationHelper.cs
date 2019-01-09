using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ValidationHelper.Attributes;
using ValidationHelper.Models;

namespace ValidationHelper
{
    public static class ValidationHelper
    {
        const string NullException = " null or empty";
        const string LengthException = " exceed length";
        const string InvalidGsmNo = " invalid gsm number";

        public static ValidationResult CheckModel<T>(T obj, bool ignoreWhenPrompted = false)
        {
            ValidationResult result = new ValidationResult
            {
                IsValid = true
            };

            if (obj == null)
            {
                result.IsValid = false;
                result.Error = "Model null";
                return result;
            }

            PropertyInfo[] propertyInfos =
                obj.GetType().GetProperties().ToArray();

            foreach (var property in propertyInfos.Where(x => x.PropertyType == typeof(string)))
            {
                object[] attrs = property.GetCustomAttributes(false);
                foreach (var attr in attrs)
                {
                    SmartStringAttribute attrSmartString = attr as SmartStringAttribute;
                    #region StringAttribute
                    if (attrSmartString != null)
                    {
                        if (attrSmartString.IgnoreWhenPrompted && ignoreWhenPrompted) continue;

                        //Null kontrolüne istendi.
                        if (attrSmartString.NotEmptyOrNullOrWhiteSpace)
                        {
                            object objValue = property.GetValue(obj);

                            //Null ise hata
                            if (string.IsNullOrEmpty(objValue?.ToString()) || string.IsNullOrWhiteSpace(objValue.ToString()))
                            {
                                result.IsValid = false;
                                result.Error = property.Name + NullException;
                                return result;
                            }

                            string value = objValue.ToString();
                            //Sadece digit olması istediyse
                            if (attrSmartString.OnlyDigit)
                            {
                                value = new string(value.Where(char.IsDigit).ToArray());
                            }

                            //Substring ile formatlandıysa
                            if (attrSmartString.SubStringIndex != -1 && attrSmartString.SubStringIndex > 0)
                            {
                                if (value.Length >= attrSmartString.SubStringIndex)
                                {
                                    value = value.Substring(value.Length - attrSmartString.SubStringIndex);
                                }
                                else
                                {
                                    result.IsValid = false;
                                    result.Error = property.Name + InvalidGsmNo;
                                    return result;
                                }
                            }

                            //Değeri değiştirilmesi istendiyse
                            if (attrSmartString.ChangeProperty)
                            {
                                property.SetValue(obj, Convert.ChangeType(value, property.PropertyType));
                            }

                            //Min length küçükse
                            if (attrSmartString.MinLength != -1 && attrSmartString.MinLength > 0)
                            {
                                if (value.Length < attrSmartString.MinLength)
                                {
                                    result.IsValid = false;
                                    result.Error = property.Name + LengthException;
                                    return result;
                                }
                            }

                            //Max length geçerse
                            if (attrSmartString.MaxLength != -1 && attrSmartString.MaxLength > 0)
                            {
                                if (value.Length > attrSmartString.MaxLength)
                                {
                                    result.IsValid = false;
                                    result.Error = property.Name + LengthException;
                                    return result;
                                }
                            }

                            //Regex Pattern istendi ise
                            if (attrSmartString.RegexPattern != null)
                            {
                                if (Regex.Match(value, attrSmartString.RegexPattern).Success) continue;
                                result.IsValid = false;
                                result.Error = property.Name + LengthException;
                                return result;
                            }
                        }
                    }
                    #endregion
                }
            }

            foreach (var property in propertyInfos.Where(x => x.PropertyType == typeof(DateTime) || x.PropertyType == typeof(DateTime?)))
            {
                object[] attrs = property.GetCustomAttributes(false);
                foreach (var attr in attrs)
                {
                    SmartDateTimeAttribute attrSmartDate = attr as SmartDateTimeAttribute;
                    if (attrSmartDate != null)
                    {
                        var objValue = property.GetValue(obj) as DateTime?;
                        if (attrSmartDate.NotNullOrDefault)
                        {
                            if (objValue == null || objValue.Value == default(DateTime))
                            {
                                result.IsValid = false;
                                result.Error = property.Name + NullException;
                                return result;
                            }

                            if (attrSmartDate.GreaterOrEqualThanTomorrow)
                            {
                                if (objValue.Value.Date <= DateTime.Today.Date)
                                {
                                    result.IsValid = false;
                                    result.Error = property.Name + "invalid date";
                                    return result;
                                }
                            }
                        }
                        if (attrSmartDate.ChangeIfDefault)
                        {
                            objValue = new DateTime(attrSmartDate.Year, attrSmartDate.Month,
                                attrSmartDate.Day);
                            //objValue = new DateTime(attrSmartDate.Year, attrSmartDate.Month, attrSmartDate.Day);
                            //property.SetValue(obj, Convert.ChangeType(objValue, property.PropertyType));
                            property.SetValue(obj, objValue, null);
                        }
                    }
                }
            }

            propertyInfos =
               obj.GetType().GetProperties().Where(x => x.PropertyType != typeof(string)).ToArray();

            foreach (var property in propertyInfos)
            {
                object[] attrs = property.GetCustomAttributes(false);
                foreach (var attr in attrs)
                {
                    SmartChildClassAttribute attrNestedNotNull = attr as SmartChildClassAttribute;
                    #region ChildClass
                    if (attrNestedNotNull != null)
                    {
                        if (attrNestedNotNull.IgnoreWhenPrompted && ignoreWhenPrompted) continue;

                        var nestObj = property.GetValue(obj);

                        if (attrNestedNotNull.NotNull)
                        {
                            if (nestObj == null)
                            {
                                result.IsValid = false;
                                result.Error = property.Name + NullException;
                                return result;
                            }

                            if (attrNestedNotNull.IsCollection)
                            {
                                var enumerables = nestObj as ICollection;
                                if (enumerables != null)
                                {
                                    if (enumerables.Count < attrNestedNotNull.MinCollectionCount)
                                    {
                                        result.IsValid = false;
                                        result.Error = $"{property.Name} at least {attrNestedNotNull.MinCollectionCount}";
                                        return result;
                                    }

                                    foreach (var enumerable in enumerables)
                                    {
                                        result = CheckModel(enumerable);

                                        if (!result.IsValid)
                                        {
                                            return result;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                result = CheckModel(nestObj);

                                if (!result.IsValid)
                                {
                                    return result;
                                }
                            }
                        }
                    }
                    #endregion
                }
            }
            return result;
        }
    }
}