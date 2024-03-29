using JWT;
using System;
using System.Net;
using UnityEngine;
using System.Text;
using Newtonsoft.Json;
using JWT.Serializers;
using AncestryResource;
using System.Collections;
using AccessTokenResource;
using CurrentUserResource;
using IdentityTokenResource;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

public class FamilySearchAuth : MonoBehaviour
{
    public static FamilySearchAuth Instance;

#if UNITY_WEBGL
    [DllImport("__Internal")]
    private static extern void StartAuthentication(string authRequest);
#elif UNITY_EDITOR || UNITY_STANDALONE_WIN
    private HttpListener httpListener;
#endif
    private Uri baseUri;
    private string codeVerifier;
    private string codeChallenge;
    private AccessTokenJson accessToken;
    private string authorizationCode;
    private UnityWebRequest webRequest;
    private string outState;
    private UnityWebRequestAsyncOperation asyncOperation;

    private bool exchangeFinished = true;

    [SerializeField] private Environment devEnvironment;
    [SerializeField] private string clientId;
    [SerializeField] private string WebCallbackLocation;
    [SerializeField] private string PcCallbackUri;
    [SerializeField] private string PcListernerUri;

    private enum Environment
    {
        Production,
        Beta,
        Integration
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void SetBaseURL(string setType)
    {
        if (setType == "Auth")
        {
            switch (devEnvironment)
            {
                case Environment.Production:
                    baseUri = new Uri("https://ident.familysearch.org/cis-web/oauth2/v3/authorization");
                    break;
                case Environment.Beta:
                    baseUri = new Uri("https://identbeta.familysearch.org/cis-web/oauth2/v3/authorization");
                    break;
                case Environment.Integration:
                    baseUri = new Uri("https://identint.familysearch.org/cis-web/oauth2/v3/authorization");
                    break;
            }
        }
        else if (setType == "Token")
        {
            switch (devEnvironment)
            {
                case Environment.Production:
                    baseUri = new Uri("https://ident.familysearch.org/cis-web/oauth2/v3/token");
                    break;
                case Environment.Beta:
                    baseUri = new Uri("https://identbeta.familysearch.org/cis-web/oauth2/v3/token");
                    break;
                case Environment.Integration:
                    baseUri = new Uri("https://identint.familysearch.org/cis-web/oauth2/v3/token");
                    break;
            }
        }
        else if (setType == "Regular")
        {
            switch (devEnvironment)
            {
                case Environment.Production:
                    baseUri = new Uri("https://api.familysearch.org/");
                    break;
                case Environment.Beta:
                    baseUri = new Uri("https://apibeta.familysearch.org/");
                    break;
                case Environment.Integration:
                    baseUri = new Uri("https://api-integ.familysearch.org/");
                    break;
            }
        }
    }

    private static string GenerateRandom(uint length)
    {
        byte[] bytes = new byte[length];
        RandomNumberGenerator.Create().GetBytes(bytes);
        return EncodeNoPadding(bytes);
    }

    private static string EncodeNoPadding(byte[] buffer)
    {
        string toEncode = Convert.ToBase64String(buffer);

        toEncode = toEncode.Replace("+", "-");
        toEncode = toEncode.Replace("/", "_");

        toEncode = toEncode.Replace("=", "");

        return toEncode;
    }

    private static byte[] GenerateSha256(string inputString)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(inputString);
        SHA256 sha256 = SHA256.Create();
        return sha256.ComputeHash(bytes);
    }

    public void InitAuth()
    {
        SetBaseURL("Auth");

        int randomState = UnityEngine.Random.Range(2000000, 3000000);
        outState = randomState.ToString();

        codeVerifier = GenerateRandom(32);
        codeChallenge = EncodeNoPadding(GenerateSha256(codeVerifier));

        StartOAuth();
    }

    private void StartOAuth()
    {
#if UNITY_WEBGL
        string authRequest = string.Format("{0}?client_id={1}&redirect_uri={2}&response_type=code&state={3}&code_challenge={4}&code_challenge_method=S256&scope=openid%20profile%20email%20qualifies_for_affiliate_account%20country",
        baseUri,
        clientId,
        WebCallbackLocation,
        outState,
        codeChallenge);

        StartAuthentication(authRequest);

#elif UNITY_EDITOR || UNITY_STANDALONE_WIN

        httpListener = new HttpListener();
        httpListener.Prefixes.Add(PcListernerUri);

        string authRequest = string.Format("{0}?client_id={1}&redirect_uri={2}&response_type=code&state={3}&code_challenge={4}&code_challenge_method=S256&scope=openid%20profile%20email%20qualifies_for_affiliate_account%20country",
         baseUri,
         clientId,
         PcCallbackUri,
         outState,
         codeChallenge);

        httpListener.Start();

        Application.OpenURL(authRequest);

        HttpListenerContext context = httpListener.GetContext();
        HttpListenerResponse response = context.Response;

        string responseString = "<HTML><HEAD><SCRIPT>window.close();</SCRIPT></HEAD><BODY></BODY></HTML>";
        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
        response.ContentLength64 = buffer.Length;
        System.IO.Stream output = response.OutputStream;
        output.Write(buffer, 0, buffer.Length);
        output.Close();

        httpListener.Stop();

        authorizationCode = context.Request.QueryString.Get("code");
        string inState = context.Request.QueryString.Get("state");

        if (inState == outState)
        {
            StartCoroutine(ExchangeCodeForToken());
        }
#endif
    }

    public void GetAuthResultsWeb(string result)
    {
        string[] response = result.Split('?');

        response = response[1].Split("&");

        string[] authResponse = response[0].Split("=");
        string[] stateResponse = response[1].Split("=");

        if (stateResponse[1] == outState)
        {
            authorizationCode = authResponse[1];
            StartCoroutine(ExchangeCodeForToken());
        }
    }

    private IEnumerator ExchangeCodeForToken()
    {
        do
        {
            if (string.IsNullOrEmpty(authorizationCode))
            {
                yield return new WaitForSeconds(0.001f);
            }
        }
        while (string.IsNullOrEmpty(authorizationCode));

        SetBaseURL("Token");

        exchangeFinished = false;

        Dictionary<string, string> formData = new Dictionary<string, string>();
        formData.Add("code", authorizationCode);
        formData.Add("grant_type", "authorization_code");
        formData.Add("client_id", clientId);
        formData.Add("code_verifier", codeVerifier);

        authorizationCode = null;

        webRequest = UnityWebRequest.Post(baseUri, formData);
        webRequest.SetRequestHeader("Accept", "application/json");
        webRequest.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");

        webRequest.downloadHandler = new DownloadHandlerBuffer();

        asyncOperation = webRequest.SendWebRequest();
        asyncOperation.completed += (AsyncOperation op) => { ExchangeResponse(asyncOperation); };
    }

    private void ExchangeResponse(UnityWebRequestAsyncOperation op)
    {
        if (op.webRequest.responseCode == 200)
        {
            accessToken = JsonConvert.DeserializeObject<AccessTokenJson>(op.webRequest.downloadHandler.text);
            exchangeFinished = true;

            StartCoroutine(DecodeJWT());
        }
    }

    private IEnumerator DecodeJWT()
    {
        IJsonSerializer serializer = new JsonNetSerializer();
        IBase64UrlEncoder urlEncoder = new JwtBase64UrlEncoder();
        IJwtDecoder decoder = new JwtDecoder(serializer, urlEncoder);

        string jwtString = decoder.Decode(accessToken.id_token);

        IdentityJson identityToken = JsonConvert.DeserializeObject<IdentityJson>(jwtString);

        yield return new WaitForSeconds(1.0f);
        StartCoroutine(GetCurrentUser());
    }

    private IEnumerator GetCurrentUser()
    {
        do
        {
            yield return new WaitForSeconds(0.001f);
        }
        while (!exchangeFinished);

        SetBaseURL("Regular");

        string apiRoute = "platform/users/current";
        string request = string.Format("{0}{1}", baseUri, apiRoute);

        webRequest = UnityWebRequest.Get(request);
        webRequest.SetRequestHeader("Accept", "application/json");
        webRequest.SetRequestHeader("Authorization", "Bearer " + accessToken.access_token);

        webRequest.downloadHandler = new DownloadHandlerBuffer();

        asyncOperation = webRequest.SendWebRequest();
        asyncOperation.completed += (AsyncOperation op) => { CurrentUserResponse(asyncOperation); };
    }

    private void CurrentUserResponse(UnityWebRequestAsyncOperation op)
    {
        if (op.webRequest.responseCode == 200)
        {
            CurrentUserJson currentUser = JsonConvert.DeserializeObject<CurrentUserJson>(op.webRequest.downloadHandler.text);
            GetAncestry(currentUser.users[0].personId);
        }
    }

    private void GetAncestry(string userPid)
    {
        string apiRoute = "platform/tree/ancestry";
        string person = "?person=" + userPid;
        string generations = "&generations=4";
        string request = string.Format("{0}{1}{2}{3}",
            baseUri,
            apiRoute,
            person,
            generations);

        webRequest = UnityWebRequest.Get(request);
        webRequest.SetRequestHeader("Accept", "application/json");
        webRequest.SetRequestHeader("Authorization", "Bearer " + accessToken.access_token);

        webRequest.downloadHandler = new DownloadHandlerBuffer();

        asyncOperation = webRequest.SendWebRequest();
        asyncOperation.completed += (AsyncOperation op) => { AncestryResponse(asyncOperation); };
    }

    private void AncestryResponse(UnityWebRequestAsyncOperation op)
    {
        if (op.webRequest.responseCode == 200)
        {
            AncestryJson ancestryJson = JsonConvert.DeserializeObject<AncestryJson>(op.webRequest.downloadHandler.text);

            for (int i = 0; i < ancestryJson.persons.Count; i++)
            {
                Debug.Log(ancestryJson.persons[i].display.name);
                Debug.Log(ancestryJson.persons[i].display.gender);
                Debug.Log(ancestryJson.persons[i].id);
                Debug.Log(ancestryJson.persons[i].display.lifespan);
            }
        }
    }
}
