// Services/MacKeychain.cs

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AkademiTrack.Services
{
    internal static class MacKeychain
    {
        private const string ServiceName = "AkademiTrack";

        // SecKeychainAddGenericPassword
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainAddGenericPassword(
            IntPtr keychain,
            uint serviceNameLength,
            byte[] serviceName,
            uint accountNameLength,
            byte[] accountName,
            uint passwordLength,
            byte[] passwordData,
            out IntPtr itemRef);

        // SecKeychainFindGenericPassword
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainFindGenericPassword(
            IntPtr keychain,
            uint serviceNameLength,
            byte[] serviceName,
            uint accountNameLength,
            byte[] accountName,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr itemRef);

        // SecKeychainItemDelete
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);

        // SecKeychainItemModifyAttributeAndData (for update)
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemModifyAttributeAndData(
            IntPtr itemRef,
            IntPtr attrList,
            uint passwordLength,
            byte[] passwordData);

        // Helper to free memory returned by the API
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        private const int errSecSuccess = 0;
        private const int errSecItemNotFound = -25300;
        private const int errSecDuplicateItem = -25299;

        /// <summary>
        /// Saves (or updates) a secret in the macOS Keychain.
        /// </summary>
        public static void Save(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                throw new ArgumentException("Key and value must be non-empty.");

            // First try to update an existing entry
            if (TryUpdate(key, value))
                return;

            // No entry → create a new one
            var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
            var keyBytes     = Encoding.UTF8.GetBytes(key);
            var valueBytes   = Encoding.UTF8.GetBytes(value);

            int err = SecKeychainAddGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length, serviceBytes,
                (uint)keyBytes.Length,     keyBytes,
                (uint)valueBytes.Length,   valueBytes,
                out _);

            if (err == errSecDuplicateItem)
            {
                TryUpdate(key, value);
                return;
            }

            if (err != errSecSuccess)
                throw new InvalidOperationException($"Keychain error {err} while saving '{key}'.");
        }

        private static bool TryUpdate(string key, string value)
        {
            var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
            var keyBytes     = Encoding.UTF8.GetBytes(key);
            var valueBytes   = Encoding.UTF8.GetBytes(value);

            int err = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length, serviceBytes,
                (uint)keyBytes.Length,     keyBytes,
                out _, out IntPtr dataPtr, out IntPtr itemRef);

            if (err != errSecSuccess)
                return false;

            try
            {
                err = SecKeychainItemModifyAttributeAndData(
                    itemRef,
                    IntPtr.Zero,
                    (uint)valueBytes.Length,
                    valueBytes);

                if (err != errSecSuccess)
                    throw new InvalidOperationException($"Keychain update error {err} for '{key}'.");
                return true;
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, dataPtr);
            }
        }

        /// <summary>
        /// Reads a secret from the Keychain.
        /// </summary>
        public static string? Load(string key)
        {
            var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
            var keyBytes     = Encoding.UTF8.GetBytes(key);

            int err = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length, serviceBytes,
                (uint)keyBytes.Length,     keyBytes,
                out uint length, out IntPtr dataPtr, out _);

            if (err == errSecItemNotFound)
                return null;

            if (err != errSecSuccess)
                throw new InvalidOperationException($"Keychain read error {err} for '{key}'.");

            try
            {
                var bytes = new byte[length];
                Marshal.Copy(dataPtr, bytes, 0, (int)length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, dataPtr);
            }
        }

        /// <summary>
        /// Deletes a secret.
        /// </summary>
        public static void Delete(string key)
        {
            var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
            var keyBytes     = Encoding.UTF8.GetBytes(key);

            int err = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length, serviceBytes,
                (uint)keyBytes.Length,     keyBytes,
                out _, out _, out IntPtr itemRef);

            if (err == errSecItemNotFound)
                return;

            if (err == errSecSuccess)
                SecKeychainItemDelete(itemRef);
        }
    }
}