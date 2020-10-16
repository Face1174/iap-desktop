﻿//
// Copyright 2020 Google LLC
//
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
//

using Google.Solutions.Common.Diagnostics;
using Google.Solutions.Common.Util;
using Google.Solutions.IapDesktop.Application.Services.Adapters;
using Google.Solutions.IapDesktop.Application.Util;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Google.Solutions.IapDesktop.Application.Services.SecureConnect
{
    public class SecureConnectEnrollment : IDeviceEnrollment
    {
        private const string DeviceCertIssuer = "CN=Google Endpoint Verification";

        private readonly ISecureConnectAdapter adapter;
        private readonly ICertificateStoreAdapter certificateStore;

        public DeviceEnrollmentState State { get; private set; }
        public X509Certificate2 Certificate { get; private set; }

        private SecureConnectEnrollment(
            ISecureConnectAdapter adapter,
            ICertificateStoreAdapter certificateStore)
        {
            this.adapter = adapter;
            this.certificateStore = certificateStore;

            // Initialize to a default state. The real initialization
            // happens in RefreshAsync().

            this.State = DeviceEnrollmentState.NotInstalled;
            this.Certificate = null;
        }

        //---------------------------------------------------------------------
        // Privates.
        //---------------------------------------------------------------------

        public async Task RefreshAsync(string userId)
        {
            using (TraceSources.IapDesktop.TraceMethod().WithParameters(userId))
            {
                if (!(await this.adapter.IsInstalledAsync()
                    .ConfigureAwait(false)))
                {
                    this.State = DeviceEnrollmentState.NotInstalled;
                    return;
                }

                if (await this.adapter.IsDeviceEnrolledForUserAsync(userId)
                    .ConfigureAwait(false))
                {
                    TraceSources.IapDesktop.TraceVerbose("Device enrolled for user {0}", userId);

                    // Get information about certificate.
                    var deviceInfo = await this.adapter.GetDeviceInfoAsync()
                        .ConfigureAwait(false);
                    var thumbprints = deviceInfo.CertificateThumbprints.ToHashSet();

                    TraceSources.IapDesktop.TraceVerbose(
                        "Device certificate thumbprints: {0}", 
                        string.Join(",", thumbprints));

                    var certificate = this.certificateStore.ListCertitficates(
                            DeviceCertIssuer,
                            DeviceCertIssuer)
                        .Where(c => thumbprints.Contains(c.ThumbprintSha256()))
                        .FirstOrDefault();

                    if (certificate != null)
                    {
                        TraceSources.IapDesktop.TraceVerbose(
                            "Device certificate found in certificate store");

                        this.State = DeviceEnrollmentState.Enrolled;
                        this.Certificate = certificate;
                    }
                    else
                    {
                        // Device enrolled, but no certificate found - as device
                        // certificates are not a mandatory part of an enrollment,
                        // this is a common case.

                        TraceSources.IapDesktop.TraceInformation(
                            "Device enrolled, but no device certificate provisioned");

                        this.State = DeviceEnrollmentState.EnrolledWithoutCertificate;
                        this.Certificate = null;
                    }
                }
                else
                {
                    TraceSources.IapDesktop.TraceInformation(
                        "Endpoint Verification installed, but device not enrolled");

                    this.State = DeviceEnrollmentState.NotEnrolled;
                    this.Certificate = null;
                }
            }
        }

        //---------------------------------------------------------------------
        // Publics.
        //---------------------------------------------------------------------

        public static async Task<SecureConnectEnrollment> CreateEnrollmentAsync(
            ISecureConnectAdapter adapter,
            ICertificateStoreAdapter certificateStore,
            string userId)
        {
            var enrollment = new SecureConnectEnrollment(adapter, certificateStore);
            await enrollment.RefreshAsync(userId)
                .ConfigureAwait(false);
            return enrollment;
        }
    }
}