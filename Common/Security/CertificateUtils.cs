using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.Security
{
    public static class CertificateUtils
    {
        /// <summary>
        /// Checks if the certificate is installed.
        /// </summary>
        /// <param name="thumbprint">The thumbprint of the certificate to check.</param>
        /// <returns>True if the certificate is found; otherwise, false.</returns>
        public static bool IsCertificateInstalled(string thumbprint)
        {
            // Using "using" statement ensures the store is closed correctly (IDisposable)
            using X509Store store = new(StoreName.Root, StoreLocation.LocalMachine);
            try
            {
                store.Open(OpenFlags.ReadOnly); // ReadOnly is sufficient for checking
                X509Certificate2Collection collection = store.Certificates;
                X509Certificate2Collection fcollection = collection.Find(X509FindType.FindByThumbprint, thumbprint, false);

                return fcollection.Count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"An exception occurred while checking certificate {thumbprint}.", LogLevel.Error, ex);
                return false;
            }
        }

        /// <summary>
        /// Installs a certificate from the specified file path.
        /// </summary>
        /// <param name="certificatePath">The full path to the certificate file.</param>
        public static void InstallCertificate(string certificatePath)
        {
            using X509Store store = new(StoreName.Root, StoreLocation.LocalMachine);
            try
            {
                // ReadWrite is usually required to add certificates
                store.Open(OpenFlags.ReadWrite);

                if (File.Exists(certificatePath))
                {
                    // Importing the certificate
                    var cert = new X509Certificate2(certificatePath);
                    store.Add(cert);
                }
                else
                {
                    WriteLog($"Certificate file not found: {certificatePath}", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"An exception occurred while installing certificate {certificatePath}.", LogLevel.Error, ex);
                throw;
            }
        }

        /// <summary>
        /// Uninstalls a certificate by its thumbprint.
        /// </summary>
        /// <param name="thumbprint">The thumbprint of the certificate to remove.</param>
        public static void UninstallCertificate(string thumbprint)
        {
            using X509Store store = new(StoreName.Root, StoreLocation.LocalMachine);
            try
            {
                store.Open(OpenFlags.ReadWrite);
                X509Certificate2Collection collection = store.Certificates;
                X509Certificate2Collection fcollection = collection.Find(X509FindType.FindByThumbprint, thumbprint, false);

                if (fcollection.Count > 0)
                {
                    store.RemoveRange(fcollection);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"An exception occurred while uninstalling certificate {thumbprint}.", LogLevel.Error, ex);
                throw;
            }
        }
    }
}
