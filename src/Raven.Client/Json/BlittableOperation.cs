using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Raven.Client.Documents.Session;
using Sparrow.Json;

namespace Raven.Client.Json
{
    internal static class BlittableOperation
    {
        private static readonly Lazy<JsonOperationContext> Context = new Lazy<JsonOperationContext>(JsonOperationContext.ShortTermSingleUse);

        private static readonly LazyStringValue LastModified;
        private static readonly LazyStringValue Collection;
        private static readonly LazyStringValue ChangeVector;
        private static readonly LazyStringValue Id;

        static BlittableOperation()
        {
            LastModified = Context.Value.GetLazyString(Constants.Documents.Metadata.LastModified);
            Collection = Context.Value.GetLazyString(Constants.Documents.Metadata.Collection);
            ChangeVector = Context.Value.GetLazyString(Constants.Documents.Metadata.ChangeVector);
            Id = Context.Value.GetLazyString(Constants.Documents.Metadata.Id);
        }

        public static bool EntityChanged(BlittableJsonReaderObject newObj, DocumentInfo documentInfo, IDictionary<string, DocumentsChanges[]> changes)
        {
            var docChanges = changes != null ? new List<DocumentsChanges>() : null;

            if (documentInfo.IsNewDocument == false && documentInfo.Document != null)
                return CompareBlittable(documentInfo.Id, documentInfo.Document, newObj, changes, docChanges);

            if (changes == null)
                return true;

            NewChange(null, null, null, docChanges, DocumentsChanges.ChangeType.DocumentAdded);
            changes[documentInfo.Id] = docChanges.ToArray();
            return true;
        }

        private static bool CompareBlittable(string id, BlittableJsonReaderObject originalBlittable,
            BlittableJsonReaderObject newBlittable, IDictionary<string, DocumentsChanges[]> changes,
            List<DocumentsChanges> docChanges)
        {
            BlittableJsonReaderObject.AssertNoModifications(originalBlittable, id, assertChildren: false);
            BlittableJsonReaderObject.AssertNoModifications(newBlittable, id, assertChildren: false);

            var newBlittableProps = newBlittable.GetPropertyNames();
            var oldBlittableProps = originalBlittable.GetPropertyNames();
            var newFields = newBlittableProps.Except(oldBlittableProps);
            var removedFields = oldBlittableProps.Except(newBlittableProps);

            var propertiesIds = newBlittable.GetPropertiesByInsertionOrder();

            foreach (var field in removedFields)
            {
                if (changes == null)
                    return true;
                NewChange(field, null, null, docChanges, DocumentsChanges.ChangeType.RemovedField);
            }

            var newProp = new BlittableJsonReaderObject.PropertyDetails();
            var oldProp = new BlittableJsonReaderObject.PropertyDetails();

            foreach (var propId in propertiesIds)
            {
                newBlittable.GetPropertyByIndex(propId, ref newProp);

                if (newProp.Name.Equals(LastModified) ||
                    newProp.Name.Equals(Collection) ||
                    newProp.Name.Equals(ChangeVector) ||
                    newProp.Name.Equals(Id))
                    continue;

                if (newFields.Contains(newProp.Name))
                {
                    if (changes == null)
                        return true;
                    NewChange(newProp.Name, newProp.Value, null, docChanges, DocumentsChanges.ChangeType.NewField);
                    continue;
                }

                var oldPropId = originalBlittable.GetPropertyIndex(newProp.Name);
                originalBlittable.GetPropertyByIndex(oldPropId, ref oldProp);

                switch ((newProp.Token & BlittableJsonReaderBase.TypesMask))
                {
                    case BlittableJsonToken.Integer:
                    case BlittableJsonToken.Boolean:
                    case BlittableJsonToken.LazyNumber:
                    case BlittableJsonToken.CompressedString:
                    case BlittableJsonToken.String:
                        if (newProp.Value.Equals(oldProp.Value))
                            break;

                        if (changes == null)
                            return true;
                        NewChange(newProp.Name, newProp.Value, oldProp.Value, docChanges,
                            DocumentsChanges.ChangeType.FieldChanged);
                        break;
                    case BlittableJsonToken.Null:
                        break;
                    case BlittableJsonToken.StartArray:
                        var newArray = newProp.Value as BlittableJsonReaderArray;
                        var oldArray = oldProp.Value as BlittableJsonReaderArray;

                        if ((newArray == null) || (oldArray == null))
                            throw new InvalidDataException("Invalid blittable");

                        var changed = CompareBlittableArray(id, oldArray, newArray, changes, docChanges, newProp.Name);
                        if (changed == false)
                            break;

                        if (changes == null)
                            return true;

                        break;
                    case BlittableJsonToken.StartObject:
                        {
                            changed = CompareBlittable(id, oldProp.Value as BlittableJsonReaderObject,
                                newProp.Value as BlittableJsonReaderObject, changes, docChanges);
                            if ((changes == null) && (changed))
                                return true;
                            break;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if ((changes == null) || (docChanges.Count <= 0)) return false;

            changes[id] = docChanges.ToArray();
            return true;
        }

        private static bool CompareBlittableArray(string id, BlittableJsonReaderArray oldArray, BlittableJsonReaderArray newArray, IDictionary<string, DocumentsChanges[]> changes, List<DocumentsChanges> docChanges, LazyStringValue propName)
        {
            // if we don't care about the changes
            if (oldArray.Length != newArray.Length && changes == null)
                return true;

            var position = 0;
            var changed = false;
            while (position < oldArray.Length && position < newArray.Length)
            {
                switch (oldArray[position])
                {
                    case BlittableJsonReaderObject bjro1:
                        if (newArray[position] is BlittableJsonReaderObject bjro2)
                        {
                            changed |= CompareBlittable(id, bjro1, bjro2, changes, docChanges);
                        }
                        else
                        {
                            changed = true;
                            if (changes != null)
                            {
                                NewChange(propName, newArray[position], oldArray[position], docChanges,
                                    DocumentsChanges.ChangeType.ArrayValueChanged);
                            }

                        }
                        break;
                    case BlittableJsonReaderArray bjra1:
                        if (newArray[position] is BlittableJsonReaderArray bjra2)
                        {
                            changed |= CompareBlittableArray(id, bjra1, bjra2, changes, docChanges, propName);
                        }
                        else
                        {
                            changed = true;
                            if (changes != null)
                            {
                                NewChange(propName, newArray[position], oldArray[position], docChanges,
                                    DocumentsChanges.ChangeType.ArrayValueChanged);
                            }
                        }
                        break;
                    case null:
                        if (newArray[position] != null)
                        {
                            changed = true;
                            if (changes != null)
                            {
                                NewChange(propName, newArray[position], oldArray[position], docChanges,
                                    DocumentsChanges.ChangeType.ArrayValueChanged);
                            }
                        }
                        break;
                    default:
                        if (oldArray[position].Equals(newArray[position]) == false)
                        {
                            if (changes != null)
                            {
                                NewChange(propName, newArray[position], oldArray[position], docChanges,
                                    DocumentsChanges.ChangeType.ArrayValueChanged);
                            }
                            changed = true;
                        }
                        break;
                }

                position++;
            }

            if (changes == null)
                return changed;

            // if one of the arrays is larger than the other
            while (position < oldArray.Length)
            {
                NewChange(propName, null, oldArray[position], docChanges,
                    DocumentsChanges.ChangeType.ArrayValueRemoved);
                position++;
            }

            while (position < newArray.Length)
            {
                NewChange(propName, newArray[position], null, docChanges,
                    DocumentsChanges.ChangeType.ArrayValueAdded);
                position++;
            }

            return changed;
        }

        private static void NewChange(string name, object newValue, object oldValue, List<DocumentsChanges> docChanges, DocumentsChanges.ChangeType change)
        {
            docChanges.Add(new DocumentsChanges
            {
                FieldName = name,
                FieldNewValue = newValue,
                FieldOldValue = oldValue,
                Change = change
            });
        }
    }
}
