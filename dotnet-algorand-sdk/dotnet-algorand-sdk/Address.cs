﻿using System;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Crypto.Parameters;
using Newtonsoft.Json;
using System.Collections.Generic;


namespace Algorand
{
    /// <summary>
    /// Address represents a serializable 32-byte length Algorand address.
    /// </summary>
    [JsonConverter(typeof(BytesConverter))]
    public class Address
    {
        /// <summary>
        /// The length of an address. Equal to the size of a SHA256 checksum.
        /// </summary>
        public const int LEN_BYTES = 32;

        /// <summary>
        /// the underlying bytes
        /// </summary>
        public byte[] Bytes { get; private set; }
        // the length of checksum to append
        private const int CHECKSUM_LEN_BYTES = 4;
        // expected length of base32-encoded checksum-appended addresses
        private const int EXPECTED_STR_ENCODED_LEN = 58;
        // prefix for signing bytes
        private static readonly byte[] BYTES_SIGN_PREFIX = Encoding.UTF8.GetBytes("MX");
        // prefix for hashing application ID
        private static readonly byte[] APP_ID_PREFIX = Encoding.UTF8.GetBytes("appID");

        /// <summary>
        /// Create a new address from a byte array.
        /// </summary>
        /// <param name="bytes">array of 32 bytes</param>
        [JsonConstructor]
        public Address(byte[] bytes)
        {
            if (bytes == null)
            {
                this.Bytes = new byte[LEN_BYTES];
                return;
            }
            if (bytes.Length != LEN_BYTES)
            {
                throw new ArgumentException(string.Format("Given address length is not {0}", LEN_BYTES));
            }
            this.Bytes = bytes;
        }

        /// <summary>
        /// default values for serializer to ignore
        /// </summary>
        public Address()
        {
            this.Bytes = new byte[LEN_BYTES];
        }
        /**
         * 
         * @param 
         */
        /// <summary>
        /// Create a new address from an encoded string(encoded by encodeAsString)
        /// </summary>
        /// <param name="encodedAddress">Encoded Address</param>
        public Address(string encodedAddress) //throws NoSuchAlgorithmException
        {
            //Objects.requireNonNull(encodedAddr, "address must not be null");
            if (encodedAddress == null)
            {
                throw new Exception("address must not be null");
            }
            if (IsValid(encodedAddress))
            {
                this.Bytes = GetAdressBytes(encodedAddress);
            }
            else
            {
                throw new ArgumentException("The address is not valid");
            }
        }

        private byte[] GetAdressBytes(string encodedAddr)
        {
            byte[] checksumAddr = Base32.DecodeFromString(encodedAddr);
            return JavaHelper<byte>.ArrayCopyOf(checksumAddr, LEN_BYTES);
        }
        public string ToHex()
        {
            return "0x"+BitConverter.ToString(this.Bytes).Replace("-", "");
        }
        /// <summary>
        /// check if the address is valid
        /// </summary>
        /// <param name="encodedAddress">Address</param>
        /// <returns>valid or not</returns>
        public static bool IsValid(string encodedAddress)
        {
            // interpret as base32
            var checksumAddr = Base32.DecodeFromString(encodedAddress).ToList(); 
            if (checksumAddr.Count != LEN_BYTES + CHECKSUM_LEN_BYTES)
            {
                return false;
            }
            // split into checksum
            
            byte[] checksum = checksumAddr.GetRange(LEN_BYTES, checksumAddr.Count - LEN_BYTES).ToArray();            
            byte[] addr = checksumAddr.GetRange(0, LEN_BYTES).ToArray();

            // compute expected checksum
            var hashedAddr = Digester.Digest(addr).ToList();            
            byte[] expectedChecksum = hashedAddr.GetRange(LEN_BYTES - CHECKSUM_LEN_BYTES, 
                hashedAddr.Count - LEN_BYTES + CHECKSUM_LEN_BYTES).ToArray();

            // compare
            if (Enumerable.SequenceEqual(checksum, expectedChecksum))            
                return true;
            
            else            
                return false;            
        }
        /// <summary>
        /// EncodeAsString converts the address to a human-readable representation, with
        /// a 4-byte checksum appended at the end, using SHA256. Note that string representations
        /// of addresses generated by different SDKs may not be compatible.
        /// </summary>
        /// <returns>the encoded address string</returns>
        public string EncodeAsString()
        {
            // compute sha512/256 checksum, and take the last 4 bytes as the checksum
            var checksum = Digester.Digest(Bytes).ToList().GetRange(LEN_BYTES - CHECKSUM_LEN_BYTES, CHECKSUM_LEN_BYTES);

            // concat the hashed address and the bytes
            var checksumAddress = Enumerable.Concat(Bytes, checksum);
            string res = Base32.EncodeToString(checksumAddress.ToArray(), false);
            if (res.Length != EXPECTED_STR_ENCODED_LEN)
            {
                throw new ArgumentException("unexpected address length " + res.Length);
            }
            return res;
        }
        /// <summary>
        /// verifyBytes verifies that the signature for the message is valid for the public key.
        /// The message should have been prepended with "MX" when signing.
        /// </summary>
        /// <param name="message">the message that was signed</param>
        /// <param name="signature">signature</param>
        /// <returns>true if the signature is valid</returns>
        public bool VerifyBytes(byte[] message, Signature signature)
        {
            var pk = new Ed25519PublicKeyParameters(this.Bytes, 0);
            // prepend the message prefix
            List<byte> prefixBytes = new List<byte>(BYTES_SIGN_PREFIX);
            prefixBytes.AddRange(message);

            // verify signature
            // Generate new signature            
            var signer = new Ed25519Signer();
            signer.Init(false, pk);
            signer.BlockUpdate(prefixBytes.ToArray(), 0, prefixBytes.ToArray().Length);
            return signer.VerifySignature(signature.Bytes);
        }
        public override string ToString()
        {
            return this.EncodeAsString();
        }
        public override bool Equals(object obj)
        {            
            if (obj is Address && Enumerable.SequenceEqual(this.Bytes, (obj as Address).Bytes))
                return true;
            else
                return false;

        }
        public override int GetHashCode()
        {
            return this.Bytes.GetHashCode();
        }


        /// <summary>
        /// Get the escrow address of an application.
        /// </summary>
        /// <param name="appID">appID The ID of the application</param>
        /// <returns>The address corresponding to that application's escrow account.</returns>
        public static Address ForApplication(ulong appID) //throws NoSuchAlgorithmException, IOException
        {
            var buffer=Utils.CombineBytes(APP_ID_PREFIX, appID.ToBigEndianBytes());
            return new Address(Digester.Digest(buffer));
        }
}
    /// <summary>
    /// MultisigAddress is a convenience class for handling multisignature public identities.
    /// </summary>
    [JsonConverter(typeof(MultisigAddressConverter))]
    public class MultisigAddress
    {
        //public const int KEY_LEN_BYTES = 32;
        [JsonProperty]
        public int version;
        [JsonProperty]
        public int threshold;
        [JsonProperty]
        public List<Ed25519PublicKeyParameters> publicKeys = new List<Ed25519PublicKeyParameters>();

        private static readonly byte[] PREFIX = Encoding.UTF8.GetBytes("MultisigAddr");
        /// <summary>
        /// 
        /// </summary>
        /// <param name="version"></param>
        /// <param name="threshold"></param>
        /// <param name="publicKeys"></param>
        public MultisigAddress(int version, int threshold,
                List<Ed25519PublicKeyParameters> publicKeys)
        {
            this.version = version;
            this.threshold = threshold;
            this.publicKeys.AddRange(publicKeys);

            if (this.version != 1)
            {
                throw new ArgumentException("Unknown msig version");
            }

            if (
                this.threshold == 0 ||
                this.publicKeys.Count == 0 ||
                this.threshold > this.publicKeys.Count
            )
            {
                throw new ArgumentException("Invalid threshold");
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="version"></param>
        /// <param name="threshold"></param>
        /// <param name="publicKeys"></param>
        [JsonConstructor]
        public MultisigAddress(int version, int threshold, List<byte[]> publicKeys)
            : this(version, threshold, publicKeys.ConvertAll(key => new Ed25519PublicKeyParameters(key, 0)))
        { }
        /// <summary>
        /// building an address object helps us generate string representations
        /// </summary>
        /// <returns>the address</returns>
        public Address ToAddress()
        {
            List<byte> hashable = new List<byte>(PREFIX)
            {
                Convert.ToByte(this.version),
                Convert.ToByte(this.threshold)
            };
            foreach (var key in publicKeys)
                hashable.AddRange(key.GetEncoded());

            return new Address(Digester.Digest(hashable.ToArray()));
        }
        public override string ToString()
        {
            return this.ToAddress().ToString();
        }
        public override bool Equals(object obj)
        {
            if(obj is MultisigAddress mAddress)
            {
                if (publicKeys.Count == mAddress.publicKeys.Count) 
                {
                    int keyCount = publicKeys.Count;
                    if (keyCount != 0)                     
                    {
                        var publicKeyEqual = true;
                        for(int i = 0; i < keyCount; i++)
                        {
                            publicKeyEqual &= publicKeys[i].Equals(mAddress.publicKeys[i]);
                            if (!publicKeyEqual) return false;
                        }
                    }
                    return version == mAddress.version && threshold == mAddress.threshold;
                }
                else return false;
                    //return base.Equals(obj);
            }
            return false;
        }
    }
}