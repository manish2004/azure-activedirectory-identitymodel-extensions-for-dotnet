﻿//-----------------------------------------------------------------------
// Copyright (c) Microsoft Open Technologies, Inc.
// All Rights Reserved
// Apache License 2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//-----------------------------------------------------------------------

namespace System.IdentityModel.Tokens
{
    using Microsoft.IdentityModel;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.IdentityModel.Protocols.WSTrust;
    using System.Security.Claims;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;

    /// <summary>
    /// A <see cref="SecurityTokenHandler"/> designed for creating and validating Json Web Tokens. See http://tools.ietf.org/html/draft-ietf-oauth-json-web-token-07.
    /// </summary>
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:ElementsMustBeDocumented", Justification = "Suppressed for private or internal fields.")]
    public class JwtSecurityTokenHandler : SecurityTokenHandler, ISecurityTokenValidator
    {
        // the Sts pipeline expects the first identifier to be a string that 
        // Uri.TryCreate( tokenIdentifiers[0], UriKind.Absolute, out result ) will be true.
        // if that is not true, sts's using the .Net sts class will start failing.

        private static IDictionary<string, string> outboundAlgorithmMap = new Dictionary<string, string>() 
                                                                            { 
                                                                                { SecurityAlgorithms.RsaSha256Signature, JwtConstants.Algorithms.RSA_SHA256 }, 
                                                                                { SecurityAlgorithms.HmacSha256Signature, JwtConstants.Algorithms.HMAC_SHA256 },
                                                                            };

        private static IDictionary<string, string> inboundAlgorithmMap = new Dictionary<string, string>() 
                                                                            { 
                                                                                { JwtConstants.Algorithms.RSA_SHA256, SecurityAlgorithms.RsaSha256Signature }, 
                                                                                { JwtConstants.Algorithms.HMAC_SHA256, SecurityAlgorithms.HmacSha256Signature },
                                                                            };

        private static IDictionary<string, string> inboundClaimTypeMap = ClaimTypeMapping.InboundClaimTypeMap;
        private static IDictionary<string, string> outboundClaimTypeMap = ClaimTypeMapping.OutboundClaimTypeMap;
        private static string shortClaimTypeProperty = ClaimProperties.Namespace + "/ShortTypeName";
        private static ISet<string> inboundClaimFilter = ClaimTypeMapping.InboundClaimFilter;
        private static string[] tokenTypeIdentifiers = { JwtConstants.TokenTypeAlt, JwtConstants.TokenType };
        private SignatureProviderFactory signatureProviderFactory = new SignatureProviderFactory();
        private Int32 _maximumTokenSizeInBytes = TokenValidationParameters.DefaultMaximumTokenSizeInBytes;

        private int DefaultTokenLifetimeInMinutes = 60;

        /// <summary>
        /// Initializes a new instance of the <see cref="JwtSecurityTokenHandler"/> class.
        /// </summary>
        public JwtSecurityTokenHandler()
        {
        }

        /// <summary>Gets or sets the <see cref="IDictionary{TKey, TValue}"/> used to map Inbound Cryptographic Algorithms.</summary>
        /// <remarks>Strings that describe Cryptographic Algorithms that are understood by the runtime are not necessarily the same values used in the JsonWebToken specification.
        /// <para>When a <see cref="JwtSecurityToken"/> signature is validated, the algorithm is obtained from the HeaderParameter { alg, 'value' }.  
        /// The 'value' is translated according to this mapping and the translated 'value' is used when performing cryptographic operations.</para>
        /// <para>Default mapping is:</para>
        /// <para>&#160;&#160;&#160;&#160;RS256 => http://www.w3.org/2001/04/xmldsig-more#rsa-sha256 </para>
        /// <para>&#160;&#160;&#160;&#160;HS256 => http://www.w3.org/2001/04/xmldsig-more#hmac-sha256 </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">'value' is null.</exception>
        public static IDictionary<string, string> InboundAlgorithmMap
        {
            get
            {
                return inboundAlgorithmMap;
            }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                inboundAlgorithmMap = value;
            }
        }

        /// <summary>Gets or sets the <see cref="IDictionary{TKey, TValue}"/> used to map Outbound Cryptographic Algorithms.</summary>
        /// <remarks>Strings that describe Cryptographic Algorithms understood by the runtime are not necessarily the same in the JsonWebToken specification.
        /// <para>This property contains mappings the will be used to when creating a <see cref="JwtHeader"/> and setting the HeaderParameter { alg, 'value' }. 
        /// The 'value' set is translated according to this mapping.
        /// </para>
        /// <para>Default mapping is:</para>
        /// <para>&#160;&#160;&#160;&#160;http://www.w3.org/2001/04/xmldsig-more#rsa-sha256  => RS256</para>
        /// <para>&#160;&#160;&#160;&#160;http://www.w3.org/2001/04/xmldsig-more#hmac-sha256 => HS256</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">'value' is null.</exception>
        public static IDictionary<string, string> OutboundAlgorithmMap
        {
            get
            {
                return outboundAlgorithmMap;
            }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                inboundAlgorithmMap = value;
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="InboundClaimTypeMap"/> that is used when setting the <see cref="Claim.Type"/> for claims in the <see cref="ClaimsPrincipal"/> extracted when validating a <see cref="JwtSecurityToken"/>. 
        /// <para>The <see cref="Claim.Type"/> is set to the JSON claim 'name' after translating using this mapping.</para>
        /// </summary>
        /// <exception cref="ArgumentNullException">'value is null.</exception>
        public static IDictionary<string, string> InboundClaimTypeMap
        {
            get
            {
                return inboundClaimTypeMap;
            }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                inboundClaimTypeMap = value;
            }
        }

        /// <summary>
        /// <para>Gets or sets the <see cref="OutboundClaimTypeMap"/> that is used when creating a <see cref="JwtSecurityToken"/> from <see cref="Claim"/>(s).</para>
        /// <para>The JSON claim 'name' value is set to <see cref="Claim.Type"/> after translating using this mapping.</para>
        /// </summary>
        /// <remarks>This mapping is applied only when using <see cref="JwtPayload.AddClaim"/> or <see cref="JwtPayload.AddClaims"/>. Adding values directly will not result in translation.</remarks>
        /// <exception cref="ArgumentNullException">'value is null.</exception>
        public static IDictionary<string, string> OutboundClaimTypeMap
        {
            get
            {
                return outboundClaimTypeMap;
            }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                outboundClaimTypeMap = value;
            }
        }

        /// <summary>Gets or sets the <see cref="ISet{String}"/> used to filter claims when populating a <see cref="ClaimsIdentity"/> claims form a <see cref="JwtSecurityToken"/>.
        /// When a <see cref="JwtSecurityToken"/> is validated, claims with types found in this <see cref="ISet{String}"/> will not be added to the <see cref="ClaimsIdentity"/>.</summary>
        /// <exception cref="ArgumentNullException">'value' is null.</exception>
        public static ISet<string> InboundClaimFilter
        {
            get
            {
                return inboundClaimFilter;
            }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                inboundClaimFilter = value;
            }
        }

        /// <summary>
        /// Gets or sets the property name of <see cref="Claim.Properties"/> the will contain the original JSON claim 'name' if a mapping occurred when the <see cref="Claim"/>(s) were created.
        /// <para>See <seealso cref="InboundClaimTypeMap"/> for more information.</para>
        /// </summary>
        /// <exception cref="ArgumentException">if <see cref="string"/>.IsIsNullOrWhiteSpace('value') is true.</exception>
        public static string ShortClaimTypeProperty
        {
            get
            {
                return shortClaimTypeProperty;
            }

            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10000, "value"));
                }

                shortClaimTypeProperty = value;
            }
        }

        /// <summary>
        /// Returns 'true' which indicates this instance can validate a <see cref="JwtSecurityToken"/>.
        /// </summary>
        public override bool CanValidateToken
        {
            get { return true; }
        }

        /// <summary>
        /// Returns 'true', which indicates this instance can write <see cref="JwtSecurityToken"/>.
        /// </summary>
        public override bool CanWriteToken
        {
            get { return true; }
        }

        /// <summary>
        /// Gets and sets the maximum size in bytes, that a will be processed.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">'value' less than 1.</exception>
        public Int32 MaximumTokenSizeInBytes
        {
            get
            {
                return _maximumTokenSizeInBytes;
            }

            set
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(string.Format(CultureInfo.InvariantCulture, ErrorMessages.IDX10101, value.ToString(CultureInfo.InvariantCulture)));
                }

                _maximumTokenSizeInBytes = value;
            }
        }

        /// <summary>
        /// Obsolete method, use <see cref="TokenValidationParameters"/> when processing tokens.
        /// </summary>
        /// <exception cref="NotSupportedException"> use <see cref="TokenValidationParameters"/>. when processing tokens.</exception>
        public override void LoadCustomConfiguration(XmlNodeList nodelist)
        {
            throw new NotSupportedException(ErrorMessages.IDX11004);
        }

        /// <summary>
        /// Gets or sets the <see cref="SignatureProviderFactory"/> for creating <see cref="SignatureProvider"/>(s).
        /// </summary>
        /// <remarks>This extensibility point can be used to insert custom <see cref="SignatureProvider"/>(s).
        /// <para><see cref="System.IdentityModel.Tokens.SignatureProviderFactory.CreateForVerifying(SecurityKey, string)"/> is called to obtain a <see cref="SignatureProvider"/>(s) when needed.</para></remarks>
        /// <exception cref="ArgumentNullException">'value' is null.</exception>
        public SignatureProviderFactory SignatureProviderFactory
        {
            get
            {
                return this.signatureProviderFactory;
            }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                this.signatureProviderFactory = value;
            }
        }

        /// <summary>
        /// Gets the <see cref="Type"/> supported by this handler.
        /// </summary>
        public override Type TokenType
        {
            get { return typeof(JwtSecurityToken); }
        }

        /// <summary>
        /// Determines if the <see cref="XmlReader"/> is positioned on a well formed &lt;BinarySecurityToken> element.
        /// </summary>
        /// <param name="reader"><see cref="XmlReader"/> positioned at xml.</param>
        /// <returns>
        /// <para>'true' if the reader is positioned at an element &lt;BinarySecurityToken>.
        /// in the namespace: 'http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd'</para>
        /// <para>With an attribute of 'valueType' equal to one of: </para>
        /// <para>&#160;&#160;&#160;&#160;"urn:ietf:params:oauth:token-type:jwt", "JWT" </para>
        /// <para>
        /// For example: &lt;wsse:BinarySecurityToken valueType = "JWT"> ...
        /// </para>
        /// 'false' otherwise.
        /// </returns>
        /// <remarks>The 'EncodingType' attribute is optional, if it is set, it must be equal to: "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary".</remarks>
        /// <exception cref="ArgumentNullException">'reader' is null.</exception>
        public override bool CanReadToken(XmlReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }

            reader.MoveToContent();

            if (reader.IsStartElement(WSSecurityConstantsInternal.Elements.BinarySecurityToken, WSSecurityConstantsInternal.Namespace))
            {
                string valueType = reader.GetAttribute(WSSecurityConstantsInternal.Attributes.ValueType, null);
                string encodingType = reader.GetAttribute(WSSecurityConstantsInternal.Attributes.EncodingType, null);

                if (encodingType != null && !StringComparer.Ordinal.Equals(encodingType, WSSecurityConstantsInternal.Base64EncodingType))
                {
                    return false;
                }

                if (valueType != null && StringComparer.Ordinal.Equals(valueType, JwtConstants.TokenTypeAlt))
                {
                    return true;
                }

                if (valueType != null && StringComparer.OrdinalIgnoreCase.Equals(valueType, JwtConstants.TokenType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines if the string is a well formed Json Web token (see http://tools.ietf.org/html/draft-ietf-oauth-json-web-token-07)
        /// </summary>
        /// <param name="tokenString">string that should represent a valid JSON Web Token.</param>
        /// <remarks>Uses <see cref="Regex.IsMatch(string, string)"/>( token, @"^[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]*$" ).
        /// </remarks>
        /// <returns>
        /// <para>'true' if the token is in JSON compact serialization format.</para>
        /// <para>'false' if token.Length * 2 >  <see cref="MaximumTokenSizeInBytes"/>.</para>
        /// </returns>
        /// <exception cref="ArgumentNullException">'tokenString' is null.</exception>
        public override bool CanReadToken(string tokenString)
        {
            if (tokenString == null)
            {
                throw new ArgumentNullException("tokenString");
            }

            if (tokenString.Length * 2 > this.MaximumTokenSizeInBytes)
            {
                return false;
            }

            return Regex.IsMatch(tokenString, JwtConstants.JsonCompactSerializationRegex);
        }

        /// <summary>
        /// Creating <see cref="SecurityKeyIdentifierClause"/> is not NotSupported.
        /// </summary>
        /// <exception cref="NotSupportedException"> to create a <see cref="SecurityKeyIdentifierClause"/>.</exception>
        public override SecurityKeyIdentifierClause CreateSecurityTokenReference(SecurityToken token, bool attached)
        {
            throw new NotSupportedException(ErrorMessages.IDX11005);
        }

        /// <summary>
        /// Creates a <see cref="JwtSecurityToken"/> based on values found in the <see cref="SecurityTokenDescriptor"/>.
        /// </summary>
        /// <param name="tokenDescriptor">Contains the parameters used to create the token.</param>
        /// <returns>A <see cref="JwtSecurityToken"/>.</returns>
        /// <remarks>
        /// If <see cref="SecurityTokenDescriptor.SigningCredentials"/> is not null, <see cref="JwtSecurityToken.RawData"/> will be signed.
        /// </remarks>
        /// <exception cref="ArgumentNullException">'tokenDescriptor' is null.</exception>
        public override SecurityToken CreateToken(SecurityTokenDescriptor tokenDescriptor)
        {
            if (tokenDescriptor == null)
            {
                throw new ArgumentNullException("tokenDescriptor");
            }

            return this.CreateToken(tokenDescriptor.TokenIssuerName, tokenDescriptor.AppliesToAddress, tokenDescriptor.Subject, tokenDescriptor.Lifetime, tokenDescriptor.SigningCredentials);
        }

        /// <summary>
        /// Uses the <see cref="JwtSecurityToken(JwtHeader, JwtPayload, string )"/> constructor, first creating the <see cref="JwtHeader"/> and <see cref="JwtPayload"/>.
        /// <para>If <see cref="SigningCredentials"/> is not null, <see cref="JwtSecurityToken.RawData"/> will be signed.</para>
        /// </summary>
        /// <param name="issuer">the issuer of the token.</param>
        /// <param name="audience">the expected audience for this token</param>
        /// <param name="subject">the source of the <see cref="Claim"/>(s) for this token.</param>
        /// <param name="lifetime">the creation and expiration times for this token.</param>
        /// <param name="signingCredentials">contains cryptographic material for generating a signature.</param>
        /// <param name="signatureProvider">optional <see cref="SignatureProvider"/>.</param>
        /// <remarks>If <see cref="ClaimsIdentity.Actor"/> is not null, then a claim { actort, 'value' } will be added to the payload. <see cref="CreateActorValue"/> for details on how the value is created.
        /// <para>See <seealso cref="JwtHeader"/> for details on how the HeaderParameters are added to the header.</para>
        /// <para>See <seealso cref="JwtPayload"/> for details on how the values are added to the payload.</para></remarks>       
        /// <para>If signautureProvider is not null, then it will be used to create the signature and <see cref="System.IdentityModel.Tokens.SignatureProviderFactory.CreateForSigning( SecurityKey, string )"/> will not be called.</para>
        /// <returns>A <see cref="JwtSecurityToken"/>.</returns>
        public virtual JwtSecurityToken CreateToken(string issuer = null, string audience = null, ClaimsIdentity subject = null, Lifetime lifetime = null, SigningCredentials signingCredentials = null, SignatureProvider signatureProvider = null)
        {
            JwtPayload payload = new JwtPayload(issuer, audience, subject == null ? null : subject.Claims, lifetime ?? new Lifetime(new DateTime?(DateTime.UtcNow), new DateTime?(DateTime.UtcNow + TimeSpan.FromMinutes(DefaultTokenLifetimeInMinutes))));
            JwtHeader header = new JwtHeader(signingCredentials);

            if (subject != null && subject.Actor != null)
            {
                payload.AddClaim(new Claim(JwtConstants.ReservedClaims.Actort, this.CreateActorValue(subject.Actor)));
            }

            string signature = string.Empty;
            string signingInput = string.Concat(header.Encode(), ".", payload.Encode());
            if( signatureProvider != null)
            {
                signature = Base64UrlEncoder.Encode(this.CreateSignature(signingInput, null, null, signatureProvider));
            }
            else if (signingCredentials != null)
            {
                signature = Base64UrlEncoder.Encode(this.CreateSignature(signingInput, signingCredentials.SigningKey, signingCredentials.SignatureAlgorithm, signatureProvider));
            }

            return new JwtSecurityToken(header, payload, string.Concat(signingInput, ".", signature));
        }

        /// <summary>
        /// Gets the token type identifier(s) supported by this handler.
        /// </summary>
        /// <returns>A collection of strings that identify the tokens this instance can handle.</returns>
        /// <remarks>When receiving a <see cref=" JwtSecurityToken"/> wrapped inside a &lt;wsse:BinarySecurityToken> element. The &lt;wsse:BinarySecurityToken> element must have the ValueType attribute set to one of these values
        /// in order for this handler to recognize that it can read the token.</remarks>
        public override string[] GetTokenTypeIdentifiers()
        {
            return tokenTypeIdentifiers;
        }

        /// <summary>
        /// Reads a JSON web token wrapped inside a WS-Security BinarySecurityToken xml element.
        /// </summary>
        /// <param name="reader">The <see cref="XmlReader"/> pointing at the jwt.</param>
        /// <returns>An instance of <see cref="JwtSecurityToken"/></returns>
        /// <remarks>First calls <see cref="JwtSecurityToken"/>.CanReadToken
        /// <para>The reader must be positioned at an element named:</para>
        /// <para>BinarySecurityToken'.
        /// in the namespace: 'http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd'
        /// with a 'ValueType' attribute equal to one of: "urn:ietf:params:oauth:token-type:jwt", "JWT".</para>
        /// <para>
        /// For example &lt;wsse:BinarySecurityToken valueType = "JWT"> ...
        /// </para>
        /// <para>
        /// The 'EncodingType' attribute is optional, if it is set, it must be equal to: "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">'reader' is null.</exception>
        /// <exception cref="ArgumentException">if <see cref="CanReadToken(XmlReader)"/> returns false.</exception>
        public override SecurityToken ReadToken(XmlReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }

            if (!this.CanReadToken(reader))
            {
                throw new ArgumentException(
                    string.Format(
                            CultureInfo.InvariantCulture,
                            JwtErrors.Jwt10203,
                            GetType().ToString(),
                            reader.ReadOuterXml(),
                            WSSecurityConstantsInternal.Elements.BinarySecurityToken,
                            WSSecurityConstantsInternal.Namespace,
                            WSSecurityConstantsInternal.Attributes.ValueType,
                            JwtConstants.TokenTypeAlt,
                            JwtConstants.TokenType));
            }

            using (XmlDictionaryReader dictionaryReader = XmlDictionaryReader.CreateDictionaryReader(reader))
            {
                string wsuId = dictionaryReader.GetAttribute(WSSecurityUtilityConstantsInternal.Attributes.Id, WSSecurityConstantsInternal.Namespace);
                JwtSecurityToken jwt = this.ReadToken(Encoding.UTF8.GetString(dictionaryReader.ReadElementContentAsBase64())) as JwtSecurityToken;
                if (wsuId != null && jwt != null)
                {
                    jwt.SetId(wsuId);
                }

                return jwt;
            }
        }

        /// <summary>
        /// Reads a token encoded in JSON Compact serialized format.
        /// </summary>
        /// <param name="securityToken">A 'JSON Web Token' (JWT) that has been encoded as a JSON object. May be signed 
        /// using 'JSON Web Signature' (JWS).</param>
        /// <remarks>
        /// The JWT must be encoded using Base64Url encoding of the UTF-8 representation of the JWT: Header, Payload and Signature. 
        /// The contents of the JWT returned are not validated in any way, the token is simply decoded. Use ValidateToken to validate the JWT.
        /// </remarks>
        /// <returns>A <see cref="JwtSecurityToken"/></returns>
        public override SecurityToken ReadToken(string securityToken)
        {
            if (securityToken == null)
            {
                throw new ArgumentNullException("securityToken");
            }

            if (securityToken.Length * 2 > this.MaximumTokenSizeInBytes)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10206, securityToken.Length, this.MaximumTokenSizeInBytes));
            }

            if (!this.CanReadToken(securityToken))
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10204, GetType(), securityToken));
            }

            return new JwtSecurityToken(securityToken);
        }

        /// <summary>
        /// Obsolete method, use <see cref="ValidateToken(String, TokenValidationParameters, out SecurityToken)"/>.
        /// </summary>
        /// <exception cref="NotSupportedException"> use <see cref="ValidateToken(String, TokenValidationParameters, out SecurityToken)"/>.</exception>
        public override ReadOnlyCollection<ClaimsIdentity> ValidateToken(SecurityToken token)
        {
            throw new NotSupportedException(JwtErrors.Jwt11000);
        }

        /// <summary>
        /// Reads and validates a token encoded in JSON Compact serialized format.
        /// </summary>
        /// <param name="securityToken">A 'JSON Web Token' (JWT) that has been encoded as a JSON object. May be signed 
        /// using 'JSON Web Signature' (JWS).</param>
        /// <param name="validationParameters">Contains validation parameters for the <see cref="JwtSecurityToken"/>.</param>
        /// <param name="validatedToken">The <see cref="JwtSecurityToken"/> that was validated.</param>
        /// <exception cref="ArgumentNullException">'securityToken' is null or whitespace.</exception>
        /// <exception cref="ArgumentNullException">'validationParameters' is null.</exception>
        /// <exception cref="ArgumentException">'securityToken.Length' > <see cref="MaximumTokenSizeInBytes"/>.</exception>
        /// <returns>A <see cref="ClaimsPrincipal"/> from the jwt. Does not include the header claims.</returns>
        public virtual ClaimsPrincipal ValidateToken(string securityToken, TokenValidationParameters validationParameters, out SecurityToken validatedToken)
        {
            if (string.IsNullOrWhiteSpace(securityToken))
            {
                throw new ArgumentNullException("securityToken");
            }

            if (validationParameters == null)
            {
                throw new ArgumentNullException("validationParameters");
            }

            if (securityToken.Length > MaximumTokenSizeInBytes)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10206, securityToken.Length, MaximumTokenSizeInBytes));
            }

            JwtSecurityToken jwt = this.ValidateSignature(securityToken, validationParameters);

            object obj = null;
            DateTime? notBefore = null;
            if (jwt.Payload.TryGetValue(JwtConstants.ReservedClaims.Nbf, out obj))
            {
                notBefore = new DateTime?(jwt.ValidFrom);
            }

            DateTime? expires = null;
            if ( jwt.Payload.TryGetValue(JwtConstants.ReservedClaims.Exp, out obj))
            {
                expires = new DateTime?(jwt.ValidTo);
            }

            if (validationParameters.LifetimeValidator != null)
            {
                validationParameters.LifetimeValidator(expires, notBefore, jwt, validationParameters);
            }
            else
            {
                this.ValidateLifetime(expires, notBefore, jwt, validationParameters);
            }

            if (validationParameters.AudienceValidator != null)
            {
                validationParameters.AudienceValidator(new string[] { jwt.Audience }, jwt, validationParameters);
            }
            else
            {
                this.ValidateAudience(new string[] { jwt.Audience }, jwt, validationParameters);
            }

            string issuer = jwt.Issuer;
            if (validationParameters.IssuerValidator != null)
            {
                issuer = validationParameters.IssuerValidator(issuer, jwt, validationParameters);
            }
            else
            {
                issuer = ValidateIssuer(issuer, jwt, validationParameters);
            }

            if (validationParameters.ValidateActor && !string.IsNullOrWhiteSpace(jwt.Actor))
            {
                SecurityToken actor = null;
                ValidateToken(jwt.Actor, validationParameters, out actor);
            }


            if (jwt.SigningKey != null)
            {
                this.ValidateIssuerSecurityKey(jwt.SigningKey, jwt, validationParameters);
            }

            validatedToken = jwt;
            return new ClaimsPrincipal(this.CreateClaimsIdentity(jwt, issuer, validationParameters));
        }

        /// <summary>
        /// Writes the <see cref="JwtSecurityToken"/> wrapped in a WS-Security BinarySecurityToken using the <see cref="XmlWriter"/>.
        /// </summary>
        /// <param name="writer"><see cref="XmlWriter"/> used to write token.</param>
        /// <param name="token">The <see cref="JwtSecurityToken"/> that will be written.</param>
        /// <exception cref="ArgumentNullException">'writer' is null.</exception>
        /// <exception cref="ArgumentNullException">'token' is null.</exception>
        /// <exception cref="ArgumentException">'token' is not a not <see cref="JwtSecurityToken"/>.</exception>
        /// <remarks>The <see cref="JwtSecurityToken"/> current contents are encoded. If <see cref="JwtSecurityToken.SigningCredentials"/> is not null, the encoding will contain a signature.</remarks>
        public override void WriteToken(XmlWriter writer, SecurityToken token)
        {
            if (writer == null)
            {
                throw new ArgumentNullException("writer");
            }

            if (token == null)
            {
                throw new ArgumentNullException("token");
            }

            if (!(token is JwtSecurityToken))
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10200, GetType(), typeof(JwtSecurityToken), token.GetType()));
            }

            byte[] rawData = Encoding.UTF8.GetBytes(this.WriteToken(token));
            writer.WriteStartElement(WSSecurityConstantsInternal.Prefix, WSSecurityConstantsInternal.Elements.BinarySecurityToken, WSSecurityConstantsInternal.Namespace);
            if (token.Id != null)
            {
                writer.WriteAttributeString(WSSecurityConstantsInternal.Prefix, WSSecurityUtilityConstantsInternal.Attributes.Id, WSSecurityConstantsInternal.Namespace, token.Id);
            }

            writer.WriteAttributeString(WSSecurityConstantsInternal.Attributes.ValueType, null, JwtConstants.TokenTypeAlt);
            writer.WriteAttributeString(WSSecurityConstantsInternal.Attributes.EncodingType, null, WSSecurityConstantsInternal.Base64EncodingType);
            writer.WriteBase64(rawData, 0, rawData.Length);
            writer.WriteEndElement();
        }

        /// <summary>
        /// Writes the <see cref="JwtSecurityToken"/> as a JSON Compact serialized format string.
        /// </summary>
        /// <param name="token"><see cref="JwtSecurityToken"/> to serialize.</param>
        /// <remarks>
        /// <para>If the <see cref="JwtSecurityToken.SigningCredentials"/> are not null, the encoding will contain a signature.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">'token' is null.</exception>
        /// <exception cref="ArgumentException">'token' is not a not <see cref="JwtSecurityToken"/>.</exception>
        /// <returns>The <see cref="JwtSecurityToken"/> as a signed (if <see cref="SigningCredentials"/> exist) encoded string.</returns>
        //TODO - need way to specify signature provider
        public override string WriteToken(SecurityToken token)
        {
            if (token == null)
            {
                throw new ArgumentNullException("token");
            }

            JwtSecurityToken jwt = token as JwtSecurityToken;
            if (jwt == null)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10200, GetType(), typeof(JwtSecurityToken), token.GetType()));
            }

            string signature = string.Empty;
            string signingInput = string.Concat(jwt.EncodedHeader, ".", jwt.EncodedPayload);

            if (jwt.SigningCredentials != null)
            {
                signature = Base64UrlEncoder.Encode(this.CreateSignature(signingInput, jwt.SigningCredentials.SigningKey, jwt.SigningCredentials.SignatureAlgorithm));
            }

            return string.Concat(signingInput, ".", signature);
        }

        /// <summary>
        /// Produces a signature over the 'input' using the <see cref="SecurityKey"/> and algorithm specified.
        /// </summary>
        /// <param name="inputString">string to be signed</param>
        /// <param name="key">the <see cref="SecurityKey"/> to use.</param>
        /// <param name="algorithm">the algorithm to use.</param>
        /// <param name="signatureProvider">if provided, the <see cref="SignatureProvider"/> will be used to sign the token</param>
        /// <returns>The signature over the bytes obtained from UTF8Encoding.GetBytes( 'input' ).</returns>
        /// <remarks>The <see cref="SignatureProvider"/> used to created the signature is obtained by calling <see cref="System.IdentityModel.Tokens.SignatureProviderFactory.CreateForSigning(SecurityKey, string)"/>.</remarks>
        /// <exception cref="ArgumentNullException">'input' is null.</exception>
        /// <exception cref="InvalidProgramException"><see cref="System.IdentityModel.Tokens.SignatureProviderFactory.CreateForSigning(SecurityKey, string)"/> returns null.</exception>
        internal byte[] CreateSignature(string inputString, SecurityKey key, string algorithm, SignatureProvider signatureProvider = null)
        {
            if (null == inputString)
            {
                throw new ArgumentNullException("inputString");
            }

            SignatureProvider provider;
            if (signatureProvider != null)
            {
                return signatureProvider.Sign(Encoding.UTF8.GetBytes(inputString));
            }
            else
            {
                provider = SignatureProviderFactory.CreateForSigning(key, algorithm);
                if (provider == null)
                {
                    throw new InvalidProgramException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10331, SignatureProviderFactory.GetType(), typeof(SignatureProvider), key == null ? "<null>" : key.GetType().ToString(), algorithm == null ? "<null>" : algorithm));
                }

                byte[] bytes = provider.Sign(Encoding.UTF8.GetBytes(inputString));
                SignatureProviderFactory.ReleaseProvider(provider);
                return bytes;
            }
        }

        private bool ValidateSignature(byte[] encodedBytes, byte[] signature, SecurityKey key, string algorithm)
        {
            // in the case that a SignatureProviderFactory can handle nulls, just don't check here.
            SignatureProvider signatureProvider = SignatureProviderFactory.CreateForVerifying(key, algorithm);
            if (signatureProvider == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10314, key == null ? TextStrings.Null : key.ToString(), algorithm == null ? TextStrings.Null : algorithm));
            }

            return signatureProvider.Verify(encodedBytes, signature);
        }

        /// <summary>
        /// Validates that the signature, if found and / or required is valid.
        /// </summary>
        /// <param name="securityToken">A 'JSON Web Token' (JWT) that has been encoded as a JSON object. May be signed 
        /// using 'JSON Web Signature' (JWS).</param>
        /// <param name="validationParameters"><see cref="TokenValidationParameters"/> that contains signing keys.</param>
        /// <exception cref="ArgumentNullException"> thrown if 'securityToken is null or whitespace.</exception>
        /// <exception cref="ArgumentNullException"> thrown if 'validationParameters is null.</exception>
        /// <exception cref="SecurityTokenValidationException"> thrown if a signature is not found and <see cref="TokenValidationParameters.RequireSignedTokens"/> is true.</exception>
        /// <exception cref="SecurityTokenSignatureKeyNotFoundException"> thrown if the 'securityToken' has a key identifier and none of the <see cref="SecurityKey"/>(s) provided result in a validated signature. 
        /// This can indicate that a key refresh is required.</exception>
        /// <exception cref="SecurityTokenInvalidSignatureException"> thrown if after trying all the <see cref="SecurityKey"/>(s), none result in a validated signture AND the 'securityToken' does not have a key identifier.</exception>
        /// <returns><see cref="JwtSecurityToken"/> that has the signature validated if securityToken was signed and <see cref="TokenValidationParameters.RequireSignedTokens"/> is true.</returns>
        /// <remarks><para>If the 'securityToken' is signed, the signature is validated even if <see cref="TokenValidationParameters.RequireSignedTokens"/> is false.</para>
        /// <para>If the 'securityToken' signature is validated, then the <see cref="JwtSecurityToken.SigningKey"/> will be set to the key that signed the 'securityToken'.</para></remarks>
        protected virtual JwtSecurityToken ValidateSignature(string securityToken, TokenValidationParameters validationParameters)
        {
            if (string.IsNullOrWhiteSpace(securityToken))
            {
                throw new ArgumentNullException("securityToken");
            }

            JwtSecurityToken jwt = this.ReadToken(securityToken) as JwtSecurityToken;

            string[] parts = securityToken.Split('.');
            byte[] encodedBytes = Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]);
            byte[] signatureBytes = Base64UrlEncoder.DecodeBytes(parts[2]);

            if (signatureBytes == null)
            {
                throw new ArgumentNullException("signatureBytes");
            }

            if (signatureBytes.Length == 0)
            {
                if (!validationParameters.RequireSignedTokens)
                {
                    return jwt;
                }

                throw new SecurityTokenValidationException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10312, jwt.RawData));
            }

            string mappedAlgorithm = jwt.Header.Alg;
            if (mappedAlgorithm != null && InboundAlgorithmMap.ContainsKey(mappedAlgorithm))
            {
                mappedAlgorithm = InboundAlgorithmMap[mappedAlgorithm];
            }

            IEnumerable<SecurityKey> securityKeys = this.RetreiveIssuerSigningKeys(securityToken, validationParameters);
            List<SecurityKey> keysThatMatchedJwtSecurityKeyIdentifier = new List<SecurityKey>();
            List<SecurityKey> keysThatDidNotMatchJwtSecurityKeyIdentifier = new List<SecurityKey>();
            SecurityKeyIdentifier jwtSigningKeyIdentifier = jwt.Header.SigningKeyIdentifier;
            Exception firstException = null;
            string keysAttempted = string.Empty;
            string exceptionString = string.Empty;

            // First run through all keys looking for a match with jwt key identifier
            foreach (SecurityKey securityKey in securityKeys)
            {
                bool matched = false;
                foreach (SecurityKeyIdentifierClause clause in jwtSigningKeyIdentifier)
                {
                    if (KeyMatchesClause(securityKey, clause))
                    {
                        matched = true;
                        keysThatMatchedJwtSecurityKeyIdentifier.Add(securityKey);
                        try
                        {
                            if (this.ValidateSignature(encodedBytes, signatureBytes, securityKey, mappedAlgorithm))
                            {
                                jwt.SigningKey = securityKey;
                                return jwt;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (DiagnosticUtility.IsFatal(ex))
                            {
                                throw;
                            }
                            
                            if (firstException == null)
                            {
                                firstException = ex;
                            }

                            exceptionString += ex.ToString();
                            exceptionString += Environment.NewLine;
                        }
                    }
                }
  
                if (!matched)
                {
                    keysThatDidNotMatchJwtSecurityKeyIdentifier.Add(securityKey);
                }
            }

            // try rest of the keys
            foreach (SecurityKey securityKey in keysThatDidNotMatchJwtSecurityKeyIdentifier)
            {
                try
                {
                    if (this.ValidateSignature(encodedBytes, signatureBytes, securityKey, mappedAlgorithm))
                    {
                        jwt.SigningKey = securityKey;
                        return jwt;
                    }
                }
                catch (Exception ex)
                {
                    if (DiagnosticUtility.IsFatal(ex))
                    {
                        throw;
                    }

                    if (firstException == null)
                    {
                        firstException = ex;
                    }

                    exceptionString += ex.ToString();
                    exceptionString += Environment.NewLine;
                }
            }

            // in the case, a key identifier was found in the jwt, but it didn't match any of the keys throw an exception that indicates
            // a refresh is probably needed.
            if (keysThatMatchedJwtSecurityKeyIdentifier.Count == 0 && jwtSigningKeyIdentifier.Count > 0)
            {
                if (firstException != null)
                {
                    throw new SecurityTokenSignatureKeyNotFoundException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10334, jwtSigningKeyIdentifier, securityToken), firstException);
                }
                else
                {
                    throw new SecurityTokenSignatureKeyNotFoundException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10334, jwtSigningKeyIdentifier, securityToken));
                }
            }

            // record all keys that were tried in the error message
            if (keysThatMatchedJwtSecurityKeyIdentifier.Count > 0 || keysThatDidNotMatchJwtSecurityKeyIdentifier.Count > 0)
            {
                keysAttempted = string.Empty;
                foreach(SecurityKey securityKey in keysThatMatchedJwtSecurityKeyIdentifier)
                {
                    keysAttempted += CreateKeyString(securityKey) + Environment.NewLine;
                }

                foreach (SecurityKey securityKey in keysThatDidNotMatchJwtSecurityKeyIdentifier)
                {
                    keysAttempted += CreateKeyString(securityKey) + Environment.NewLine;
                }
            }
            else
            {
                keysAttempted = JwtErrors.NoSecurityKeysTried;
            }

            if (exceptionString.Length > 0)
            {
                throw new SecurityTokenInvalidSignatureException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10316, keysAttempted, exceptionString, jwt.ToString()), firstException);
            }
            else
            {
                throw new SecurityTokenInvalidSignatureException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10315, keysAttempted, jwt.ToString()));
            }
        }

        private bool KeyMatchesClause(SecurityKey securityKey, SecurityKeyIdentifierClause clause)
        {
            X509SecurityKey x509SecurityKey = securityKey as X509SecurityKey;
            if (x509SecurityKey != null)
            {
                X509SecurityToken x509SecurityToken = new X509SecurityToken(x509SecurityKey.Certificate);
                if (x509SecurityToken.MatchesKeyIdentifierClause(clause))
                {
                    return true;
                }
            }

            return false;
        }

        private string CreateKeyString(SecurityKey securityKey)
        {
            if (securityKey == null)
            {
                return "null";
            }
            else 
            {
                return securityKey.ToString();
            }
        }

        /// <summary>
        /// Creates a <see cref="ClaimsIdentity"/> from a <see cref="JwtSecurityToken"/>.
        /// </summary>
        /// <param name="jwt">The <see cref="JwtSecurityToken"/> to use as a <see cref="Claim"/> source.</param>
        /// <param name="issuer">The value to set <see cref="Claim.Issuer"/></param>
        /// <param name="validationParameters"> contains parameters for validating the token.</param>
        /// <returns>A <see cref="ClaimsIdentity"/> containing the <see cref="JwtSecurityToken.Claims"/>.</returns>
        protected virtual ClaimsIdentity CreateClaimsIdentity(JwtSecurityToken jwt, string issuer, TokenValidationParameters validationParameters)
        {
            if (jwt == null)
            {
                throw new ArgumentNullException("jwt");
            }

            if (string.IsNullOrWhiteSpace(issuer))
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10333, jwt.ToString()));
            }

            string nameClaimType = null;
            if (validationParameters.NameClaimTypeRetriever != null)
            {
                nameClaimType = validationParameters.NameClaimTypeRetriever(jwt, issuer);
            }
            else
            {
                nameClaimType = validationParameters.NameClaimType;
            }

            string roleClaimType = null;
            if (validationParameters.RoleClaimTypeRetriever != null)
            {
                roleClaimType = validationParameters.RoleClaimTypeRetriever(jwt, issuer);
            }
            else
            {
                roleClaimType = validationParameters.RoleClaimType;
            }

            // TODO - check that nameClaimType || roleClaimType is not null.
            ClaimsIdentity identity = new ClaimsIdentity(validationParameters.AuthenticationType, nameClaimType, roleClaimType);
            if (validationParameters.SaveSigninToken)
            {
                if (jwt.RawData != null)
                {
                    identity.BootstrapContext = new BootstrapContext(jwt.RawData);
                }
                else
                {
                    identity.BootstrapContext = new BootstrapContext(this.WriteToken(jwt));
                }
            }

            foreach (Claim jwtClaim in jwt.Claims)
            {
                if (InboundClaimFilter.Contains(jwtClaim.Type))
                {
                    continue;
                }

                string claimType;
                bool wasMapped = false;
                if (JwtSecurityTokenHandler.InboundClaimTypeMap.ContainsKey(jwtClaim.Type))
                {
                    claimType = JwtSecurityTokenHandler.InboundClaimTypeMap[jwtClaim.Type];
                    wasMapped = true;
                }
                else
                {
                    claimType = jwtClaim.Type;
                }

                if (claimType == ClaimTypes.Actor)
                {
                    if (identity.Actor != null)
                    {
                        throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, JwtErrors.Jwt10401, JwtConstants.ReservedClaims.Actort, jwtClaim.Value));
                    }

                    if (this.CanReadToken(jwtClaim.Value))
                    {
                        JwtSecurityToken actor = this.ReadToken(jwtClaim.Value) as JwtSecurityToken;
                        identity.Actor = this.CreateClaimsIdentity(actor, issuer, validationParameters);
                    }
                    else
                    {
                        Claim claim = new Claim(claimType, jwtClaim.Value, jwtClaim.ValueType, issuer, issuer, identity);
                        if (wasMapped)
                        {
                            claim.Properties.Add(ShortClaimTypeProperty, jwtClaim.Type);
                        }

                        identity.AddClaim(claim);
                    }
                }
                else
                {
                    Claim claim = new Claim(claimType, jwtClaim.Value, jwtClaim.ValueType, issuer, issuer, identity);
                    if (wasMapped)
                    {
                        claim.Properties.Add(ShortClaimTypeProperty, jwtClaim.Type);
                    }

                    identity.AddClaim(claim);
                }
            }

            return identity;
        }

        /// <summary>
        /// Creates the 'value' for the actor claim: { actort, 'value' }
        /// </summary>
        /// <param name="actor"><see cref="ClaimsIdentity"/> as actor.</param>
        /// <returns><see cref="string"/> representing the actor.</returns>
        /// <remarks>If <see cref="ClaimsIdentity.BootstrapContext"/> is not null:
        /// <para>&#160;&#160;if 'type' is 'string', return as string.</para>
        /// <para>&#160;&#160;if 'type' is 'BootstrapContext' and 'BootstrapContext.SecurityToken' is 'JwtSecurityToken'</para>
        /// <para>&#160;&#160;&#160;&#160;if 'JwtSecurityToken.RawData' != null, return RawData.</para>        
        /// <para>&#160;&#160;&#160;&#160;else return <see cref="JwtSecurityTokenHandler.WriteToken( SecurityToken )"/>.</para>        
        /// <para>&#160;&#160;if 'BootstrapContext.Token' != null, return 'Token'.</para>
        /// <para>default: <see cref="JwtSecurityTokenHandler.WriteToken(SecurityToken)"/> new ( <see cref="JwtSecurityToken"/>( actor.Claims ).</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">'actor' is null.</exception>
        protected virtual string CreateActorValue(ClaimsIdentity actor)
        {
            if (actor == null)
            {
                throw new ArgumentNullException("actor");
            }

            if (actor.BootstrapContext != null)
            {
                string encodedJwt = actor.BootstrapContext as string;
                if (encodedJwt != null)
                {
                    return encodedJwt;
                }

                BootstrapContext bootstrapContext = actor.BootstrapContext as BootstrapContext;
                if (bootstrapContext != null)
                {
                    JwtSecurityToken jwt = bootstrapContext.SecurityToken as JwtSecurityToken;
                    if (jwt != null)
                    {
                        if (jwt.RawData != null)
                        {
                            return jwt.RawData;
                        }
                        else
                        {
                            return this.WriteToken(jwt);
                        }
                    }

                    if (bootstrapContext.Token != null)
                    {
                        return bootstrapContext.Token;
                    }
                }
            }

            return this.WriteToken(new JwtSecurityToken(claims: actor.Claims));
        }

        /// <summary>
        /// Determines if the audiences found in a <see cref="JwtSecurityToken"/> are valid.
        /// </summary>
        /// <param name="audiences">The audiences found in the <see cref="JwtSecurityToken"/>.</param>
        /// <param name="securityToken">The <see cref="JwtSecurityToken"/> being validated.</param>
        /// <param name="validationParameters"><see cref="TokenValidationParameters"/> required for validation.</param>
        /// <remarks>see <see cref="Validators.ValidateAudience"/> for additional details.</remarks>
        protected virtual void ValidateAudience(IEnumerable<string> audiences, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            Validators.ValidateAudience(audiences, securityToken, validationParameters);
        }

        /// <summary>
        /// Validates the lifetime of a <see cref="JwtSecurityToken"/>.
        /// </summary>
        /// <param name="expires">The <see cref="DateTime"/> value of the 'exp' claim if it exists in the 'jwt'.</param>
        /// <param name="notBefore">The <see cref="DateTime"/> value of the 'nbf' claim if it exists in the 'jwt'.</param>
        /// <param name="securityToken">The <see cref="JwtSecurityToken"/> being validated.</param>
        /// <param name="validationParameters"><see cref="TokenValidationParameters"/> required for validation.</param>
        /// <remarks><see cref="Validators.ValidateLifetime"/> for additional details.</remarks>
        protected virtual void ValidateLifetime(DateTime? expires, DateTime? notBefore, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            Validators.ValidateLifetime(expires, notBefore, securityToken, validationParameters);
        }

        /// <summary>
        /// Determines if an issuer found in a <see cref="JwtSecurityToken"/> is valid.
        /// </summary>
        /// <param name="issuer">The issuer to validate</param>
        /// <param name="securityToken">The <see cref="JwtSecurityToken"/> that is being validated.</param>
        /// <param name="validationParameters"><see cref="TokenValidationParameters"/> required for validation.</param>
        /// <returns>The issuer to use when creating the <see cref="Claim"/>(s) in the <see cref="ClaimsIdentity"/>.</returns>
        /// <remarks><see cref="Validators.ValidateIssuer"/> for additional details.</remarks>
        protected virtual string ValidateIssuer(string issuer, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            return Validators.ValidateIssuer(issuer, securityToken, validationParameters);
        }

        /// <summary>
        /// Produces a <see cref="IEnumerable{SecurityKey}"/> to use when validating the signature of a securityToken.
        /// </summary>
        /// <param name="securityToken"> the security token that needs to have it's signature validated validated.</param>
        /// <param name="validationParameters">A <see cref="TokenValidationParameters"/> instance that has references to multiple <see cref="SecurityKey"/>.</param>
        /// <returns>Returns a <see cref="IEnumerable{SecurityKey}"/> of the keys to use for signature validation.</returns>
        /// <exception cref="ArgumentNullException">'validationParameters' is null.</exception>
        internal IEnumerable<SecurityKey> RetreiveIssuerSigningKeys(string securityToken, TokenValidationParameters validationParameters)
        {
            if (validationParameters != null)
            {
                // gets keys from metadata
                if (validationParameters.IssuerSigningKeyRetriever != null)
                {
                    foreach (SecurityKey securityKey in validationParameters.IssuerSigningKeyRetriever(securityToken))
                    {
                        yield return securityKey;
                    }
                }

                if (validationParameters.IssuerSigningKey != null)
                {
                    yield return validationParameters.IssuerSigningKey;
                }

                if (validationParameters.IssuerSigningKeys != null)
                {
                    foreach (SecurityKey securityKey in validationParameters.IssuerSigningKeys)
                    {
                        yield return securityKey;
                    }
                }

                if (validationParameters.IssuerSigningToken != null && validationParameters.IssuerSigningToken.SecurityKeys != null)
                {
                    foreach (SecurityKey securityKey in validationParameters.IssuerSigningToken.SecurityKeys)
                    {
                        yield return securityKey;
                    }
                }

                if (validationParameters.IssuerSigningTokens != null)
                {
                    foreach (SecurityToken token in validationParameters.IssuerSigningTokens)
                    {
                        if (token.SecurityKeys != null)
                        {
                            foreach (SecurityKey securityKey in token.SecurityKeys)
                            {
                                yield return securityKey;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Validates the <see cref="JwtSecurityToken.SigningKey"/> is an expected value.
        /// </summary>
        /// <param name="securityKey">The <see cref="SecurityKey"/> that signed the <see cref="SecurityToken"/>.</param>
        /// <param name="securityToken">The <see cref="JwtSecurityToken"/> to validate.</param>
        /// <param name="validationParameters">the current <see cref="TokenValidationParameters"/>.</param>
        /// <remarks>If the <see cref="JwtSecurityToken.SigningKey"/> is a <see cref="X509SecurityKey"/> then the X509Certificate2 will be validated using <see cref="TokenValidationParameters.CertificateValidator"/>.</remarks>
        protected virtual void ValidateIssuerSecurityKey(SecurityKey securityKey, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            Validators.ValidateIssuerSecurityKey(securityKey, securityToken, validationParameters);
        }
    }
}