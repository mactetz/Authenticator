﻿using Domain.Storage;
using Microsoft.OneDrive.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;

namespace Synchronization
{
    public class OneDriveSynchronizer : ISynchronizer
    {
        private const string FILENAME = "Accounts.dat";
        private const string KEY = "P`31ba]6a'v+zu3B~oS4|Qcjzd>1,]";

        private bool _isInitialSetup;
        private string hash;
        private IOneDriveClient client;

        public bool IsInitialSetup
        {
            get
            {
                return _isInitialSetup;
            }
        }
        public OneDriveSynchronizer(IOneDriveClient client) : this(client, null)
        {
            
        }

        public OneDriveSynchronizer(IOneDriveClient client, string hash)
        {
            this.client = client;
            this.hash = hash;
        }

        public async Task Setup()
        {
            await GetFileFromOneDrive();
        }

        private async Task GetFileFromOneDrive()
        {
            try
            {
                IItemRequestBuilder builder = client.Drive.Special.AppRoot.ItemWithPath(FILENAME);
                Item file = await builder.Request().GetAsync();
                Stream contentStream = await builder.Content.Request().GetAsync();
                string content = "";

                using (var reader = new StreamReader(contentStream))
                {
                    content = await reader.ReadToEndAsync();
                }

                string[] parts = content.Split(' ');
                byte[] b = new byte[parts.Length];
                int index = 0;

                foreach (string part in parts)
                {
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        int currentNumber = int.Parse(part);
                        byte currentPart = Convert.ToByte(currentNumber);

                        b[index] = currentPart;
                    }

                    index++;
                }

                if (!string.IsNullOrWhiteSpace(content))
                {

                    byte[] bytes = Encoding.UTF32.GetBytes(content);
                    string decrypted = Decrypt(b, KEY, "12345678");

                    _isInitialSetup = false;
                }
            }
            catch (OneDriveException ex)
            {
                if (ex.Error.Code == "itemNotFound")
                {
                    _isInitialSetup = true;
                }
            }
        }

        public Stream GenerateStreamFromString(string s)
        {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        public async Task<SynchronizationResult> Synchronize()
        {
            string input = await AccountStorage.Instance.GetPlainStorageAsync();

            //var encryptString = EasyEncryption.Aes.Encrypt(input, KEY, "12345");
            //var result = EasyEncryption.Aes.Decrypt(encryptString, key, iv);

            byte[] encrypted = Encrypt(input, KEY, "12345678");
            int i = 0;

            StringBuilder builder = new StringBuilder();

            foreach (byte b in encrypted)
            {
                builder.Append(b);

                if (i < encrypted.Length - 1)
                {
                    builder.Append(" ");
                }

                i++;
            }

            string encryptedText = Encoding.UTF32.GetString(encrypted);
            Stream stream = GenerateStreamFromString(builder.ToString());

            var item = await client.Drive.Special.AppRoot
                  .ItemWithPath(FILENAME)
                  .Content.Request()
                  .PutAsync<Item>(stream);

            return new SynchronizationResult()
            {
                Successful = true
            };
        }

        public static byte[] Encrypt(string plainText, string pw, string salt)
        {
            IBuffer pwBuffer = CryptographicBuffer.ConvertStringToBinary(pw, BinaryStringEncoding.Utf8);
            IBuffer saltBuffer = CryptographicBuffer.ConvertStringToBinary(salt, BinaryStringEncoding.Utf16LE);
            IBuffer plainBuffer = CryptographicBuffer.ConvertStringToBinary(plainText, BinaryStringEncoding.Utf16LE);

            // Derive key material for password size 32 bytes for AES256 algorithm
            KeyDerivationAlgorithmProvider keyDerivationProvider = Windows.Security.Cryptography.Core.KeyDerivationAlgorithmProvider.OpenAlgorithm("PBKDF2_SHA1");
            // using salt and 1000 iterations
            KeyDerivationParameters pbkdf2Parms = KeyDerivationParameters.BuildForPbkdf2(saltBuffer, 1000);

            // create a key based on original key and derivation parmaters
            CryptographicKey keyOriginal = keyDerivationProvider.CreateKey(pwBuffer);
            IBuffer keyMaterial = CryptographicEngine.DeriveKeyMaterial(keyOriginal, pbkdf2Parms, 32);
            CryptographicKey derivedPwKey = keyDerivationProvider.CreateKey(pwBuffer);

            // derive buffer to be used for encryption salt from derived password key 
            IBuffer saltMaterial = CryptographicEngine.DeriveKeyMaterial(derivedPwKey, pbkdf2Parms, 16);

            // display the buffers – because KeyDerivationProvider always gets cleared after each use, they are very similar unforunately
            string keyMaterialString = CryptographicBuffer.EncodeToBase64String(keyMaterial);
            string saltMaterialString = CryptographicBuffer.EncodeToBase64String(saltMaterial);

            SymmetricKeyAlgorithmProvider symProvider = SymmetricKeyAlgorithmProvider.OpenAlgorithm("AES_CBC_PKCS7");
            // create symmetric key from derived password key
            CryptographicKey symmKey = symProvider.CreateSymmetricKey(keyMaterial);

            // encrypt data buffer using symmetric key and derived salt material
            IBuffer resultBuffer = CryptographicEngine.Encrypt(symmKey, plainBuffer, saltMaterial);
            byte[] result;
            CryptographicBuffer.CopyToByteArray(resultBuffer, out result);

            return result;
        }

        public static string Decrypt(byte[] encryptedData, string pw, string salt)
        {
            IBuffer pwBuffer = CryptographicBuffer.ConvertStringToBinary(pw, BinaryStringEncoding.Utf8);
            IBuffer saltBuffer = CryptographicBuffer.ConvertStringToBinary(salt, BinaryStringEncoding.Utf16LE);
            IBuffer cipherBuffer = CryptographicBuffer.CreateFromByteArray(encryptedData);

            // Derive key material for password size 32 bytes for AES256 algorithm
            KeyDerivationAlgorithmProvider keyDerivationProvider = Windows.Security.Cryptography.Core.KeyDerivationAlgorithmProvider.OpenAlgorithm("PBKDF2_SHA1");
            // using salt and 1000 iterations
            KeyDerivationParameters pbkdf2Parms = KeyDerivationParameters.BuildForPbkdf2(saltBuffer, 1000);

            // create a key based on original key and derivation parmaters
            CryptographicKey keyOriginal = keyDerivationProvider.CreateKey(pwBuffer);
            IBuffer keyMaterial = CryptographicEngine.DeriveKeyMaterial(keyOriginal, pbkdf2Parms, 32);
            CryptographicKey derivedPwKey = keyDerivationProvider.CreateKey(pwBuffer);

            // derive buffer to be used for encryption salt from derived password key 
            IBuffer saltMaterial = CryptographicEngine.DeriveKeyMaterial(derivedPwKey, pbkdf2Parms, 16);

            // display the keys – because KeyDerivationProvider always gets cleared after each use, they are very similar unforunately
            string keyMaterialString = CryptographicBuffer.EncodeToBase64String(keyMaterial);
            string saltMaterialString = CryptographicBuffer.EncodeToBase64String(saltMaterial);

            SymmetricKeyAlgorithmProvider symProvider = SymmetricKeyAlgorithmProvider.OpenAlgorithm("AES_CBC_PKCS7");
            // create symmetric key from derived password material
            CryptographicKey symmKey = symProvider.CreateSymmetricKey(keyMaterial);

            // encrypt data buffer using symmetric key and derived salt material
            IBuffer resultBuffer = CryptographicEngine.Decrypt(symmKey, cipherBuffer, saltMaterial);
            string result = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf16LE, resultBuffer);
            return result;
        }
    }
}
